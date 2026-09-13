import React, { useCallback, useEffect, useState } from 'react';
import {
  Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Link, Typography,
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import LockIcon from '@mui/icons-material/Lock';
import SecurityIcon from '@mui/icons-material/Security';
import { ApiService } from '../../services/apiService';
import { dismissSecureConnectionStep } from '../../models/Onboarding';

interface CertificateTrustStatus {
  certificateReady: boolean;
  isTrustedInSession: boolean;
  canOneClickTrust: boolean;
  isPortable: boolean;
  serverOs: string;
}

interface Props {
  onContinue: () => void;
  busy?: boolean;
}

function isFirefoxBrowser(): boolean {
  return /Firefox\//i.test(navigator.userAgent) && !/Seamonkey/i.test(navigator.userAgent);
}

function isChromiumBrowser(): boolean {
  return /Chrome\//i.test(navigator.userAgent) || /Edg\//i.test(navigator.userAgent);
}

const accordionSx = {
  bgcolor: 'rgba(10, 25, 41, 0.5)',
  color: '#cbd5e1',
  border: '1px solid #173A5E',
  '&:before': { display: 'none' },
  mb: 2,
};

const OnboardingSecureConnectionStep: React.FC<Props> = ({ onContinue, busy = false }) => {
  const [status, setStatus] = useState<CertificateTrustStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [actionBusy, setActionBusy] = useState(false);
  const [trustedNow, setTrustedNow] = useState(false);
  const [trustError, setTrustError] = useState('');

  const isTrusted = trustedNow || status?.isTrustedInSession === true;
  const canTrust = status?.canOneClickTrust && !isTrusted;
  const firefox = isFirefoxBrowser();
  const chromium = isChromiumBrowser();
  const isMac = status?.serverOs === 'macos';

  const loadStatus = useCallback(async () => {
    try {
      setStatus(await ApiService.getCertificateTrustStatus());
    } catch {
      setStatus(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadStatus();
  }, [loadStatus]);

  const preferHttp = async () => {
    try {
      await ApiService.setDashboardPreference('http');
    } catch {
      // Non-blocking  -  HTTP is the default URL.
    }
  };

  const handleTrust = async () => {
    setActionBusy(true);
    setTrustError('');
    try {
      const result = await ApiService.trustApplianceCertificate();
      if (result.trusted) {
        setTrustedNow(true);
        await loadStatus();
      } else {
        setTrustError(result.message || 'Trust did not complete. Click Yes on the Windows prompt, or continue below.');
      }
    } catch (e: unknown) {
      setTrustError(e instanceof Error ? e.message : 'Trust failed. You can continue without HTTPS.');
    } finally {
      setActionBusy(false);
    }
  };

  const handleContinue = async () => {
    await preferHttp();
    if (!isTrusted) dismissSecureConnectionStep();
    onContinue();
  };

  const handleOpenHttps = async () => {
    setActionBusy(true);
    try {
      await ApiService.setDashboardPreference('https');
      window.location.replace('https://localhost:5001/onboarding');
    } catch {
      window.location.replace('https://localhost:5001/onboarding');
    } finally {
      setActionBusy(false);
    }
  };

  const handleDownload = async () => {
    setActionBusy(true);
    try {
      await ApiService.downloadCertificate();
    } finally {
      setActionBusy(false);
    }
  };

  const disabled = busy || actionBusy || loading;

  const chromeTrustHint = (
    <Alert severity="info" sx={{ mb: 2, fontSize: 13 }}>
      {chromium ? (
        <>
          Close <strong>every Chrome window</strong> and reopen before using HTTPS  -  a refresh is not enough.
          Also confirm{' '}
          <Link href="chrome://certificate-manager/localcerts" target="_blank" rel="noopener noreferrer" sx={{ color: '#93c5fd' }}>
            chrome://certificate-manager
          </Link>
          {' '}→ <strong>Use imported local certificates from your operating system</strong> is on.
        </>
      ) : (
        <>
          Close and reopen your browser before using HTTPS if the padlock is still off.
        </>
      )}
    </Alert>
  );

  return (
    <>
      <Box sx={{ display: 'flex', justifyContent: 'center', mb: 2, color: isTrusted ? '#22c55e' : '#64748b' }}>
        <LockIcon sx={{ fontSize: 40 }} />
      </Box>

      <Typography variant="h5" fontWeight={800} textAlign="center" color="#fff" gutterBottom>
        {isTrusted ? 'HTTPS is ready (optional)' : 'Optional: HTTPS for localhost'}
      </Typography>

      <Typography textAlign="center" color="#94a3b8" sx={{ mb: 3, fontSize: 15, lineHeight: 1.6 }}>
        {isTrusted
          ? 'This PC trusts StacksAtlas Local CA. You can keep using HTTP  -  no browser warnings  -  or switch to HTTPS below.'
          : isMac
            ? 'StacksAtlas opened on HTTP (127.0.0.1). Safari or Chrome may show “Not Secure” in the address bar  -  that is normal for local HTTP and safe to continue. Trust the local CA only if you want https://localhost:5001.'
            : 'StacksAtlas works on HTTP with no warnings. Trust the local CA only if you want https://localhost:5001.'}
      </Typography>

      {isTrusted && chromeTrustHint}

      {trustError && (
        <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setTrustError('')}>
          {trustError}
        </Alert>
      )}

      {!isTrusted && canTrust && (
        <>
          <Button
            fullWidth
            variant="contained"
            size="large"
            startIcon={<SecurityIcon />}
            onClick={handleTrust}
            disabled={disabled || !status?.certificateReady}
            sx={{ mb: 1.5 }}
          >
            Trust on this PC
          </Button>
          <Typography textAlign="center" color="#64748b" fontSize={13} sx={{ mb: 2 }}>
            Windows will ask once  -  click <strong>Yes</strong>.
          </Typography>
        </>
      )}

      <Button
        fullWidth
        variant={isTrusted || !canTrust ? 'contained' : 'outlined'}
        size="large"
        onClick={handleContinue}
        disabled={disabled}
        sx={{ mb: isTrusted ? 1.5 : 2, ...(canTrust && !isTrusted ? { borderColor: '#173A5E', color: '#cbd5e1' } : {}) }}
      >
        Continue setup
      </Button>

      {isTrusted && (
        <Button
          fullWidth
          variant="outlined"
          size="large"
          onClick={handleOpenHttps}
          disabled={disabled}
          sx={{ mb: 2, borderColor: '#173A5E', color: '#94a3b8' }}
        >
          Use HTTPS dashboard
        </Button>
      )}

      <Accordion disableGutters sx={accordionSx}>
        <AccordionSummary expandIcon={<ExpandMoreIcon sx={{ color: '#94a3b8' }} />}>
          <Typography fontSize={13} fontWeight={600}>Advanced options</Typography>
        </AccordionSummary>
        <AccordionDetails sx={{ pt: 0 }}>
          <Typography fontSize={13} color="#94a3b8" sx={{ mb: 1.5 }}>
            {isMac
              ? 'On macOS, download the local CA and add it to Keychain Access (login keychain).'
              : 'Manual install for Firefox or if one-click trust fails.'}
          </Typography>
          <Button
            fullWidth
            variant="outlined"
            size="small"
            onClick={handleDownload}
            disabled={disabled}
            sx={{ mb: 1.5, borderStyle: 'dashed' }}
          >
            Download certificate file
          </Button>
          {isMac && (
            <Typography fontSize={12} color="#64748b" component="div" sx={{ mb: 1.5 }}>
              Keychain Access: double-click <strong>StacksAtlas-LocalCA.cer</strong> → add to <strong>login</strong> keychain
              → open the certificate → Trust → <strong>When using this certificate: Always Trust</strong>.
              Then restart your browser before using HTTPS.
            </Typography>
          )}
          {firefox && (
            <Typography fontSize={12} color="#64748b">
              Firefox: Settings → Privacy &amp; Security → Certificates → Import the .cer file
              and trust it for websites.
            </Typography>
          )}
        </AccordionDetails>
      </Accordion>
    </>
  );
};

export default OnboardingSecureConnectionStep;
