import React, { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Box, Button, Typography, TextField, Stepper, Step, StepLabel,
  Paper, Alert, CircularProgress, IconButton, Chip, RadioGroup,
  FormControlLabel, Radio, alpha,
} from '@mui/material';
import LayersIcon from '@mui/icons-material/Layers';
import Visibility from '@mui/icons-material/Visibility';
import VisibilityOff from '@mui/icons-material/VisibilityOff';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import { useAuth } from '../context/AuthContext';
import { ApiService } from '../services/apiService';
import {
  resolveOnboardingStep, stepIndex, stepsForStatus, shouldShowSecureConnectionStep,
} from '../models/Onboarding';
import type { OnboardingStatus, OnboardingStepId } from '../models/Onboarding';
import OnboardingSecureConnectionStep from '../components/onboarding/OnboardingSecureConnectionStep';
import { TIER_PLAN_COPY, UNLIMITED_STANDALONE_ACTIVATIONS_THRESHOLD } from '../config/licensing';
import { licenseTierLabel } from '../utils/federationMode';

const shellSx = {
  minHeight: '100vh',
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  bgcolor: '#0A1929',
  color: '#e0e0e0',
  px: 2,
  py: 4,
};

const cardSx = {
  width: '100%',
  maxWidth: 520,
  p: 4,
  borderRadius: 2,
  bgcolor: '#001E3C',
  border: '1px solid #173A5E',
  boxShadow: '0 8px 32px rgba(0,0,0,0.5)',
};

const OnboardingPage: React.FC = () => {
  const navigate = useNavigate();
  const auth = useAuth();

  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState<OnboardingStatus | null>(null);
  const [step, setStep] = useState<OnboardingStepId>('welcome');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  // Admin step
  const [username, setUsername] = useState('admin');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);

  // License step
  const [licenseKey, setLicenseKey] = useState('');
  const [hardwareId, setHardwareId] = useState('');

  // Site + role
  const [siteName, setSiteName] = useState('');
  const [fleetRole, setFleetRole] = useState<'standalone' | 'hub'>('standalone');
  const [detectedCidr, setDetectedCidr] = useState<string | null>(null);

  const refreshStatus = useCallback(async () => {
    const data = await ApiService.getOnboardingStatus();
    setStatus(data);

    let certTrusted = false;
    try {
      const cert = await ApiService.getCertificateTrustStatus();
      certTrusted = cert.isTrustedInSession === true;
    } catch {
      // If status check fails, still offer the TLS step when appropriate.
    }

    const needsTlsStep = shouldShowSecureConnectionStep(certTrusted);

    if (data.isFoundationComplete && data.isAdminConfigured) {
      if (needsTlsStep) {
        setStep('secure-connection');
        if (data.siteName) setSiteName(data.siteName);
        if (data.hardwareId) setHardwareId(data.hardwareId);
        return data;
      }
      navigate('/', { replace: true });
      return data;
    }

    const resolved = resolveOnboardingStep(data);
    if (needsTlsStep && resolved !== 'done') {
      setStep('secure-connection');
    } else {
      setStep(resolved === 'done' ? 'done' : resolved);
    }
    if (data.siteName) setSiteName(data.siteName);
    if (data.hardwareId) setHardwareId(data.hardwareId);
    return data;
  }, [navigate]);

  useEffect(() => {
    let cancelled = false;

    const init = async () => {
      try {
        setLoading(true);
        setError('');

        const waitForInit = async <T,>(fn: () => Promise<T>, attempts = 15): Promise<T> => {
          let lastError: unknown;
          for (let i = 0; i < attempts; i++) {
            try {
              return await fn();
            } catch (e: unknown) {
              lastError = e;
              const msg = e instanceof Error ? e.message : '';
              if (msg.includes('initializing') || msg.includes('503')) {
                await new Promise(r => setTimeout(r, 800));
                continue;
              }
              throw e;
            }
          }
          throw lastError;
        };

        const probe = await waitForInit(() => ApiService.getOnboardingStatus());
        if (cancelled) return;

        if (probe?.isPortable) {
          if (!auth.isAuthenticated) {
            const session = await waitForInit(() => ApiService.ensurePortableSession());
            if (session?.token) {
              auth.login(session.token, session.username);
            }
          }
        } else {
          const hw = await waitForInit(() => ApiService.getLicenseHardwareId());
          if (hw?.hardwareId) setHardwareId(hw.hardwareId);
        }

        if (cancelled) return;

        const data = await waitForInit(() => refreshStatus());
        if (cancelled) return;

        if (data?.isAdminConfigured && !auth.isAuthenticated && !data?.isPortable) {
          navigate('/login', { replace: true, state: { from: '/onboarding' } });
        }
      } catch (e: unknown) {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : 'Failed to load onboarding status');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    init();
    return () => {
      cancelled = true;
    };
    // Run once on mount  -  do not re-run when auth.login updates isAuthenticated (causes redirect storms).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const tierKey = status?.licenseTier || 'Home';
  const planCopy = TIER_PLAN_COPY[tierKey] || TIER_PLAN_COPY.Home;
  const visibleSteps = stepsForStatus(status);
  const activeStep = stepIndex(step, status);
  const siteResetRecovery = status?.pendingSiteResetRecovery;
  const hubSiteResetRecovery =
    siteResetRecovery?.hubInitiated === true && status?.isEnrolledNode;

  const handleAdminSetup = async () => {
    setError('');
    if (password !== confirmPassword) {
      setError('Passwords do not match.');
      return;
    }
    if (password.length < 8) {
      setError('Password must be at least 8 characters.');
      return;
    }
    setBusy(true);
    try {
      const result = await ApiService.setupAdmin({ username, password });
      auth.login(result.token, result.username);
      await refreshStatus();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Setup failed');
    } finally {
      setBusy(false);
    }
  };

  const handleActivateLicense = async () => {
    if (!licenseKey.trim()) {
      setError('Enter a license key or get a free key below.');
      return;
    }
    setBusy(true);
    setError('');
    try {
      const result = await ApiService.activateLicense(licenseKey.trim());
      if (!result.isActive) {
        setError(result.message || 'Activation failed');
        return;
      }
      await refreshStatus();
      setStep('plan');
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Activation failed');
    } finally {
      setBusy(false);
    }
  };

  const handleSaveSite = async () => {
    if (!siteName.trim()) {
      setError('Site name is required.');
      return;
    }
    setBusy(true);
    setError('');
    try {
      await ApiService.updateOnboarding({ siteName: siteName.trim() });
      await refreshStatus();
      if (status?.allowsHub && !status?.isPortable) {
        setStep('role');
      } else {
        setStep('network');
      }
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to save site name');
    } finally {
      setBusy(false);
    }
  };

  const loadNetworkPreview = async () => {
    try {
      const range = await ApiService.getScanningRange();
      setDetectedCidr(range?.cidr && range.cidr !== 'N/A' ? range.cidr : null);
    } catch {
      setDetectedCidr(null);
    }
  };

  useEffect(() => {
    if (step === 'network') loadNetworkPreview();
  }, [step]);

  useEffect(() => {
    if (step !== 'done') return;
    const timer = window.setTimeout(() => navigate('/', { replace: true }), 1500);
    return () => window.clearTimeout(timer);
  }, [step, navigate]);

  const handleConfirmNetwork = async () => {
    setBusy(true);
    setError('');
    try {
      if (status?.isPortable && !auth.isAuthenticated) {
        const session = await ApiService.ensurePortableSession();
        if (session?.token) {
          auth.login(session.token, session.username);
        }
      }

      if (detectedCidr) {
        const settings = await ApiService.getNetworkSettings();
        const subnets = settings?.subnets || [];
        if (!subnets.some((s: { cidr: string }) => s.cidr === detectedCidr)) {
          await ApiService.updateNetworkSettings({
            ...settings,
            subnets: [...subnets, { cidr: detectedCidr }],
          });
        }
      }
      await ApiService.updateOnboarding({ confirmNetwork: true, completeFoundation: true });
      const data = await refreshStatus();
      if (!data?.isFoundationComplete) {
        setError('Setup could not be finalized. Please try again.');
        setStep('network');
      }
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to confirm network');
      setStep('network');
    } finally {
      setBusy(false);
    }
  };

  const handleRoleContinue = async () => {
    setBusy(true);
    setError('');
    try {
      if (fleetRole === 'hub') {
        await ApiService.updateOnboarding({
          completeFoundation: true,
          hubOnlySetup: true,
        });
        setStep('done');
      } else {
        setStep('network');
      }
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to save role');
    } finally {
      setBusy(false);
    }
  };

  const handleSiteResetContinue = async () => {
    setBusy(true);
    setError('');
    try {
      await ApiService.updateOnboarding({ completeFoundation: true, skipNetworkConfirm: true });
      setStep('done');
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to complete site recovery');
    } finally {
      setBusy(false);
    }
  };

  const copyHwid = () => {
    if (hardwareId) navigator.clipboard.writeText(hardwareId);
  };

  const continueFromSecureConnection = () => {
    if (!status) {
      setStep('welcome');
      return;
    }
    if (status.isFoundationComplete && status.isAdminConfigured) {
      navigate('/', { replace: true });
      return;
    }
    const resolved = resolveOnboardingStep(status);
    setStep(resolved === 'done' ? 'done' : resolved);
  };

  if (loading) {
    return (
      <Box sx={shellSx}>
        <CircularProgress color="primary" />
        <Typography sx={{ mt: 2, color: '#64748B' }}>Loading setup...</Typography>
      </Box>
    );
  }

  return (
    <Box sx={shellSx}>
      <Box sx={{ width: '100%', maxWidth: 640, mb: 3 }}>
        {step !== 'secure-connection' && (
        <Stepper activeStep={activeStep} alternativeLabel sx={{
          '& .MuiStepLabel-label': { color: '#64748B', fontSize: 11 },
          '& .MuiStepLabel-label.Mui-active': { color: '#00A3FF' },
          '& .MuiStepLabel-label.Mui-completed': { color: '#94a3b8' },
          '& .MuiStepIcon-root': { color: '#173A5E' },
          '& .MuiStepIcon-root.Mui-active': { color: '#007FFF' },
          '& .MuiStepIcon-root.Mui-completed': { color: '#22c55e' },
        }}>
          {visibleSteps.filter(s => s.id !== 'done').map(s => (
            <Step key={s.id} completed={stepIndex(s.id, status) < activeStep}>
              <StepLabel>{s.label}</StepLabel>
            </Step>
          ))}
        </Stepper>
        )}
      </Box>

      <Paper sx={cardSx} elevation={0}>
        <Box sx={{ display: 'flex', justifyContent: 'center', mb: 2, color: '#00A3FF' }}>
          <LayersIcon sx={{ fontSize: 44 }} />
        </Box>

        {error && (
          <Alert severity="error" sx={{ mb: 3 }} onClose={() => setError('')}>
            {error}
          </Alert>
        )}

        {step === 'secure-connection' && (
          <OnboardingSecureConnectionStep onContinue={continueFromSecureConnection} busy={busy} />
        )}

        {step === 'welcome' && (
          <>
            <Typography variant="h5" fontWeight={800} textAlign="center" color="#fff" gutterBottom>
              {status?.isPortable ? 'Try StacksAtlas' : 'Set up StacksAtlas'}
            </Typography>
            <Typography textAlign="center" color="#94a3b8" sx={{ mb: 4, fontSize: 14 }}>
              {status?.isPortable
                ? 'Portable mode  -  no account or license key. Data stays in your user folder until you choose Close & remove portable data from the user menu. Federation is not available in this mode.'
                : 'License-first bootstrap  -  activate your entitlement, name your site, then start scanning.'}
            </Typography>
            <Button
              fullWidth
              variant="contained"
              size="large"
              onClick={() => {
                if (status?.isPortable) {
                  setStep('network');
                } else if (status?.isAdminConfigured) {
                  setStep(resolveOnboardingStep(status));
                } else {
                  setStep('admin');
                }
              }}
            >
              {status?.isPortable ? 'Start scanning' : status?.isAdminConfigured ? 'Continue setup' : 'Get started'}
            </Button>
          </>
        )}

        {step === 'admin' && (
          <>
            <Typography variant="h6" fontWeight={700} color="#fff" gutterBottom>
              Create administrator
            </Typography>
            <TextField
              fullWidth
              label="Username"
              value={username}
              onChange={e => setUsername(e.target.value)}
              sx={{ mb: 2, mt: 1 }}
              disabled={busy}
            />
            <TextField
              fullWidth
              label="Password"
              type={showPassword ? 'text' : 'password'}
              value={password}
              onChange={e => setPassword(e.target.value)}
              sx={{ mb: 2 }}
              disabled={busy}
              InputProps={{
                endAdornment: (
                  <IconButton onClick={() => setShowPassword(!showPassword)} edge="end" size="small">
                    {showPassword ? <VisibilityOff /> : <Visibility />}
                  </IconButton>
                ),
              }}
            />
            <TextField
              fullWidth
              label="Confirm password"
              type={showPassword ? 'text' : 'password'}
              value={confirmPassword}
              onChange={e => setConfirmPassword(e.target.value)}
              sx={{ mb: 3 }}
              disabled={busy}
            />
            <Button fullWidth variant="contained" size="large" onClick={handleAdminSetup} disabled={busy}>
              {busy ? 'Creating account...' : 'Create administrator'}
            </Button>
          </>
        )}

        {step === 'license' && (
          <>
            <Typography variant="h6" fontWeight={700} color="#fff" gutterBottom>
              Activate license
            </Typography>
            <Typography color="#94a3b8" fontSize={13} sx={{ mb: 2 }}>
              Complete Lemon Squeezy checkout to download the appliance, then paste the license key from your order confirmation email.
            </Typography>
            <Box sx={{
              display: 'flex', alignItems: 'center', gap: 1, mb: 2, p: 1.5,
              bgcolor: alpha('#0A1929', 0.8), borderRadius: 1, border: '1px solid #173A5E',
            }}>
              <Typography fontSize={12} color="#64748B" sx={{ flex: 1, wordBreak: 'break-all' }}>
                HWID: {hardwareId || '…'}
              </Typography>
              <IconButton size="small" onClick={copyHwid} disabled={!hardwareId}>
                <ContentCopyIcon fontSize="small" />
              </IconButton>
            </Box>
            <TextField
              fullWidth
              label="License key"
              value={licenseKey}
              onChange={e => setLicenseKey(e.target.value)}
              sx={{ mb: 2 }}
              disabled={busy}
              placeholder="Paste key from your order email"
            />
            <Typography fontSize={13} color="#64748B" sx={{ mb: 2 }}>
              Free keys support unlimited standalone appliances. Paid keys unlock Hub federation and enrolled site limits per your plan.
            </Typography>
            <Button fullWidth variant="contained" size="large" onClick={handleActivateLicense} disabled={busy}>
              {busy ? 'Activating...' : 'Activate license'}
            </Button>
          </>
        )}

        {step === 'plan' && (
          <>
            <Typography variant="h6" fontWeight={700} color="#fff" gutterBottom>
              Your plan
            </Typography>
            <Chip
              label={licenseTierLabel(status?.licenseTier, !!status?.licenseActive)}
              color="success"
              sx={{ mb: 2, fontWeight: 700 }}
            />
            <Typography fontWeight={600} color="#fff" gutterBottom>
              {planCopy.title}
            </Typography>
            <Typography color="#94a3b8" fontSize={14} sx={{ mb: 3 }}>
              {planCopy.body}
            </Typography>
            {status?.allowsHub && !status?.isPortable && (
              <Typography fontSize={13} color="#64748B" sx={{ mb: 2 }}>
                Fleet Hub included  -  you can choose Hub or Standalone role in a later step.
              </Typography>
            )}
            {status?.isPortable && (
              <Typography fontSize={13} color="#64748B" sx={{ mb: 2 }}>
                Portable mode  -  Hub requires a full background-service install when you are ready for production.
              </Typography>
            )}
            {!status?.allowsHub && (
              <Typography fontSize={13} color="#64748B" sx={{ mb: 2 }}>
                {(status?.maxStandaloneActivations ?? 0) >= UNLIMITED_STANDALONE_ACTIVATIONS_THRESHOLD
                  ? 'Unlimited standalone appliances on this license key.'
                  : `Up to ${status!.maxStandaloneActivations} standalone activations on this license key.`}
              </Typography>
            )}
            <Button fullWidth variant="contained" onClick={() => setStep('site')}>
              Continue
            </Button>
          </>
        )}

        {step === 'site' && (
          <>
            <Typography variant="h6" fontWeight={700} color="#fff" gutterBottom>
              Name this site
            </Typography>
            <Typography color="#94a3b8" fontSize={13} sx={{ mb: 2 }}>
              e.g. Home Lab, Main Office, Client HQ
            </Typography>
            <TextField
              fullWidth
              label="Site name"
              value={siteName}
              onChange={e => setSiteName(e.target.value)}
              sx={{ mb: 3 }}
              disabled={busy}
              autoFocus
            />
            <Button fullWidth variant="contained" onClick={handleSaveSite} disabled={busy}>
              {busy ? 'Saving...' : 'Continue'}
            </Button>
          </>
        )}

        {step === 'role' && status?.allowsHub && (
          <>
            <Typography variant="h6" fontWeight={700} color="#fff" gutterBottom>
              How will this appliance run?
            </Typography>
            <RadioGroup value={fleetRole} onChange={e => setFleetRole(e.target.value as 'standalone' | 'hub')}>
              <FormControlLabel
                value="standalone"
                control={<Radio />}
                label={
                  <Box>
                    <Typography fontWeight={600}>Standalone at this site</Typography>
                    <Typography fontSize={12} color="#64748B">Local discovery here; enroll to a Hub later from Settings.</Typography>
                  </Box>
                }
              />
              <FormControlLabel
                value="hub"
                control={<Radio />}
                label={
                  <Box>
                    <Typography fontWeight={600}>Fleet Hub</Typography>
                    <Typography fontSize={12} color="#64748B">Central pane of glass  -  configure federation in Settings after setup.</Typography>
                  </Box>
                }
              />
            </RadioGroup>
            <Button fullWidth variant="contained" sx={{ mt: 3 }} onClick={handleRoleContinue} disabled={busy}>
              Continue
            </Button>
          </>
        )}

        {step === 'site-reset' && hubSiteResetRecovery && (
          <>
            <Alert severity="info" sx={{ mb: 3 }}>
              This site was reset by{' '}
              <strong>{siteResetRecovery.initiatedByUsername || 'your Hub administrator'}</strong>
              {siteResetRecovery.initiatedAtUtc
                ? ` on ${new Date(siteResetRecovery.initiatedAtUtc).toLocaleString()}`
                : ''}
              . Fleet enrollment is preserved  -  you do not need to re-enroll with the Hub.
            </Alert>
            <Typography variant="h6" fontWeight={700} color="#fff" gutterBottom>
              Continue site setup
            </Typography>
            <Typography color="#94a3b8" fontSize={13} sx={{ mb: 3 }}>
              Local inventory was wiped. Confirm below to resume scanning and sync with{' '}
              {siteResetRecovery.hubDisplayName || 'the Hub'}.
            </Typography>
            <Button fullWidth variant="contained" size="large" onClick={handleSiteResetContinue} disabled={busy}>
              {busy ? 'Finishing...' : 'Continue to dashboard'}
            </Button>
          </>
        )}

        {step === 'network' && (
          <>
            <Typography variant="h6" fontWeight={700} color="#fff" gutterBottom>
              Confirm network
            </Typography>
            <Typography color="#94a3b8" fontSize={13} sx={{ mb: 2 }}>
              {status?.isPortable
                ? 'Portable session  -  confirm your network, then start scanning. No federation in portable mode.'
                : 'Discovery starts after you confirm. Advanced scope options are in Settings → Scanning.'}
            </Typography>
            <Box sx={{
              p: 2, mb: 3, borderRadius: 1,
              bgcolor: alpha('#0A1929', 0.8), border: '1px solid #173A5E',
            }}>
              <Typography fontSize={12} color="#64748B">Auto-detected range</Typography>
              <Typography fontWeight={700} color="#fff">
                {detectedCidr || 'No range detected  -  add scopes in Settings after setup'}
              </Typography>
            </Box>
            <Button fullWidth variant="contained" onClick={handleConfirmNetwork} disabled={busy}>
              {busy ? 'Finishing...' : 'Confirm & start monitoring'}
            </Button>
          </>
        )}

        {step === 'done' && (
          <Box textAlign="center" py={2}>
            <CheckCircleIcon sx={{ fontSize: 56, color: '#22c55e', mb: 2 }} />
            <Typography variant="h6" fontWeight={700} color="#fff">
              Setup complete
            </Typography>
            <Typography color="#94a3b8" sx={{ mt: 1 }}>
              Opening dashboard…
            </Typography>
          </Box>
        )}
      </Paper>

      <Typography sx={{ mt: 3, fontSize: 11, color: '#475569' }}>
        StacksAtlas · License-first onboarding (§7.7)
      </Typography>
    </Box>
  );
};

export default OnboardingPage;
