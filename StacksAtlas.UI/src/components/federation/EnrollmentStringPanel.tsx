import { useCallback, useEffect, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Link,
  Stack,
  TextField,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import { ApiService } from '../../services/apiService';

type EnrollTransport = 'lan' | 'tailscale';

interface EnrollmentStringPanelProps {
  /** When true, auto-generates a LAN string on mount (e.g. when enroll dialog opens). */
  active?: boolean;
  /** When false, shows hint that Tailscale identity is not configured on the Hub. */
  tailscaleConfiguredHint?: boolean;
  compact?: boolean;
  /** Shows "Remote site? Use Tailscale string →" when Direct Network is selected. */
  showRemoteSiteLink?: boolean;
  onCopied?: (text: string) => void;
  onError?: (message: string) => void;
}

export function EnrollmentStringPanel({
  active = false,
  tailscaleConfiguredHint,
  compact = false,
  showRemoteSiteLink = false,
  onCopied,
  onError,
}: EnrollmentStringPanelProps) {
  const theme = useTheme();
  const [transport, setTransport] = useState<EnrollTransport>('lan');
  const [connectionString, setConnectionString] = useState('');
  const [loading, setLoading] = useState(false);
  const [tailscaleConfigured, setTailscaleConfigured] = useState<boolean | null>(
    tailscaleConfiguredHint === undefined ? null : tailscaleConfiguredHint
  );
  const [lastHost, setLastHost] = useState<string | null>(null);
  const [initialized, setInitialized] = useState(false);

  const generate = useCallback(async (nextTransport: EnrollTransport = transport) => {
    setLoading(true);
    try {
      const res = await ApiService.getEnrollmentString({ tailscale: nextTransport === 'tailscale' });
      const hasTailscale = !!res?.tailscaleEnrollmentString;
      setTailscaleConfigured(hasTailscale);

      let str = '';
      if (nextTransport === 'tailscale') {
        str = res?.tailscaleEnrollmentString || res?.enrollmentString || '';
      } else {
        str = res?.lanEnrollmentString || res?.enrollmentString || '';
      }

      if (nextTransport === 'tailscale' && !res?.tailscaleEnrollmentString) {
        onError?.(
          'Tailscale enrollment is unavailable  -  configure Hub MagicDNS or tailnet IP in Federation → Tailscale, then try again.'
        );
      }

      setConnectionString(str);
      if (str) {
        try {
          const host = str.startsWith('sa-enroll://')
            ? new URL(str.replace('sa-enroll://', 'http://')).hostname
            : null;
          setLastHost(host);
        } catch {
          setLastHost(null);
        }
      }
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to generate enrollment string';
      onError?.(message);
    } finally {
      setLoading(false);
    }
  }, [transport, onError]);

  useEffect(() => {
    if (active && !initialized) {
      setInitialized(true);
      void generate('lan');
    }
    if (!active) {
      setInitialized(false);
    }
  }, [active, initialized, generate]);

  const handleTransportChange = (next: EnrollTransport) => {
    setTransport(next);
    void generate(next);
  };

  const handleCopy = () => {
    if (!connectionString) return;
    navigator.clipboard.writeText(connectionString);
    onCopied?.(connectionString);
  };

  return (
    <Stack spacing={compact ? 1.5 : 2}>
      <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1.5 }}>
        {transport === 'lan'
          ? 'Direct enrollment  -  uses your Hub\'s reachable IP or hostname (local subnet, WAN, or SD-WAN). Paste on the Node when it can route to that address without Tailscale.'
          : 'Remote enrollment over Tailscale  -  uses your Hub\'s MagicDNS or 100.x address. The Node must have Tailscale connected before enrolling.'}
        {' '}Token expires in 15 minutes.
      </Typography>

      <Stack direction="row" spacing={1.5} flexWrap="wrap" useFlexGap alignItems="center">
        <Button
          size="small"
          variant={transport === 'lan' ? 'contained' : 'outlined'}
          color="secondary"
          onClick={() => handleTransportChange('lan')}
          sx={{ fontWeight: 800, borderRadius: 2, flex: { xs: '1 1 100%', sm: '0 1 auto' } }}
        >
          Direct Network
        </Button>
        <Button
          size="small"
          variant={transport === 'tailscale' ? 'contained' : 'outlined'}
          color="secondary"
          onClick={() => handleTransportChange('tailscale')}
          sx={{ fontWeight: 800, borderRadius: 2, flex: { xs: '1 1 100%', sm: '0 1 auto' } }}
        >
          Tailscale (Remote)
        </Button>
        {showRemoteSiteLink && transport === 'lan' && (
          <Link
            component="button"
            type="button"
            variant="caption"
            onClick={() => handleTransportChange('tailscale')}
            sx={{ fontWeight: 800, textAlign: 'left' }}
          >
            Remote site? Use Tailscale string →
          </Link>
        )}
      </Stack>

      {transport === 'tailscale' && (tailscaleConfigured === false || tailscaleConfiguredHint === false) && (
        <Alert severity="warning" sx={{ py: 0.5 }}>
          Configure <strong>Hub MagicDNS</strong> or <strong>tailnet IP</strong> in Federation → Remote Federation via Tailscale, save, then regenerate.
        </Alert>
      )}

      {loading ? (
        <Box display="flex" justifyContent="center" alignItems="center" height={compact ? 60 : 100}>
          <Typography variant="caption" color="text.secondary">Generating token...</Typography>
        </Box>
      ) : (
        <Stack spacing={1.5}>
          <TextField
            label="Enrollment Connection String"
            multiline
            rows={compact ? 3 : 4}
            fullWidth
            value={connectionString}
            placeholder="Click Generate to create a string"
            InputProps={{
              readOnly: true,
              sx: {
                fontFamily: 'monospace',
                fontSize: '0.8rem',
                bgcolor: alpha(theme.palette.background.default, 0.4),
              },
            }}
          />
          {lastHost && (
            <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>
              Host: {lastHost}
            </Typography>
          )}
          <Stack direction="row" spacing={1} flexWrap="wrap">
            <Button
              variant="contained"
              color="secondary"
              onClick={() => void generate()}
              disabled={loading}
              sx={{ borderRadius: 2, fontWeight: 800 }}
            >
              {connectionString ? 'REGENERATE' : 'GENERATE STRING'}
            </Button>
            <Button
              variant="outlined"
              color="secondary"
              onClick={handleCopy}
              disabled={!connectionString}
              sx={{ borderRadius: 2, fontWeight: 800 }}
            >
              COPY
            </Button>
          </Stack>
        </Stack>
      )}
    </Stack>
  );
}
