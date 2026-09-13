import { useState } from 'react';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Box,
  Button,
  Link,
  Stack,
  Tab,
  Tabs,
  TextField,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import GppGoodIcon from '@mui/icons-material/GppGood';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import {
  TAILSCALE_ACL_DOCS_URL,
  TAILSCALE_ACL_HOMELAB,
  TAILSCALE_ACL_TAGGED_FLEET,
} from './tailscaleOperatorGuide';

interface TailscaleAclHelpPanelProps {
  onCopied?: (text: string) => void;
}

type AclPreset = 'tagged' | 'homelab';

export function TailscaleAclHelpPanel({ onCopied }: TailscaleAclHelpPanelProps) {
  const theme = useTheme();
  const [preset, setPreset] = useState<AclPreset>('tagged');

  const aclText = preset === 'tagged' ? TAILSCALE_ACL_TAGGED_FLEET : TAILSCALE_ACL_HOMELAB;

  const handleCopy = () => {
    navigator.clipboard.writeText(aclText);
    onCopied?.(aclText);
  };

  return (
    <Accordion
      disableGutters
      elevation={0}
      sx={{
        borderRadius: '12px !important',
        border: `1px solid ${alpha(theme.palette.divider, 0.15)}`,
        bgcolor: alpha(theme.palette.background.default, 0.35),
        '&:before': { display: 'none' },
      }}
    >
      <AccordionSummary expandIcon={<ExpandMoreIcon />} sx={{ px: 2, minHeight: 48 }}>
        <Stack direction="row" alignItems="center" spacing={1}>
          <GppGoodIcon color="secondary" sx={{ fontSize: 20 }} />
          <Typography variant="subtitle2" fontWeight={800}>
            Tailnet ACL requirements
          </Typography>
        </Stack>
      </AccordionSummary>
      <AccordionDetails sx={{ px: 2, pt: 0, pb: 2 }}>
        <Stack spacing={2}>
          <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1.55 }}>
            StacksAtlas federation uses TCP ports <strong>5001</strong> (UI/API) and <strong>5002</strong> (mTLS sync).
            Most personal and small-business tailnets use Tailscale&apos;s default policy, which already allows member-to-member traffic  -  you often do <strong>not</strong> need a custom ACL.
            Add a snippet below only if your tailnet uses restrictive ACLs or tag-based access control.
          </Typography>

          <Alert severity="info" sx={{ py: 0.5 }}>
            Assign <code>tag:stacksatlas-hub</code> to your Hub appliance and <code>tag:stacksatlas-node</code> to edge Nodes in the{' '}
            <Link href="https://login.tailscale.com/admin/machines" target="_blank" rel="noopener noreferrer">
              Tailscale machines
            </Link>{' '}
            admin, then merge the snippet below into your tailnet policy.
          </Alert>

          <Tabs
            value={preset}
            onChange={(_, value: AclPreset) => setPreset(value)}
            variant="fullWidth"
            sx={{ minHeight: 36, '& .MuiTab-root': { minHeight: 36, fontWeight: 800, fontSize: '0.75rem' } }}
          >
            <Tab label="Tagged fleet (recommended)" value="tagged" />
            <Tab label="Homelab (all members)" value="homelab" />
          </Tabs>

          <TextField
            multiline
            rows={preset === 'tagged' ? 14 : 8}
            fullWidth
            value={aclText}
            InputProps={{
              readOnly: true,
              sx: {
                fontFamily: 'monospace',
                fontSize: '0.72rem',
                bgcolor: alpha(theme.palette.background.default, 0.5),
              },
            }}
          />

          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            <Button
              size="small"
              variant="contained"
              color="secondary"
              startIcon={<ContentCopyIcon />}
              onClick={handleCopy}
              sx={{ fontWeight: 800, borderRadius: 2 }}
            >
              Copy ACL snippet
            </Button>
            <Button
              size="small"
              variant="outlined"
              color="secondary"
              href={TAILSCALE_ACL_DOCS_URL}
              target="_blank"
              rel="noopener noreferrer"
              endIcon={<OpenInNewIcon />}
              sx={{ fontWeight: 800, borderRadius: 2 }}
            >
              Tailscale ACL docs
            </Button>
          </Stack>

          <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
            <Typography component="li" variant="caption" color="text.secondary" sx={{ mb: 0.5 }}>
              Paste into Tailscale admin → Access controls → edit policy (HuJSON).
            </Typography>
            <Typography component="li" variant="caption" color="text.secondary">
              After saving, confirm reachability from a Node using the test button on this page.
            </Typography>
          </Box>
        </Stack>
      </AccordionDetails>
    </Accordion>
  );
}
