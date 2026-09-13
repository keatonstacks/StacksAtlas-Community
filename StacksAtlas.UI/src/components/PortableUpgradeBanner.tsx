import { alpha, Box, Button, Stack, Typography, useTheme } from '@mui/material';
import WorkspacePremiumIcon from '@mui/icons-material/WorkspacePremium';
import { LICENSING_LINKS, TIER_2_COPY } from '../config/licensingLinks';

interface PortableUpgradeBannerProps {
  variant?: 'dashboard' | 'compact';
  onOpenSettings?: () => void;
}

export function PortableUpgradeBanner({ variant = 'dashboard', onOpenSettings }: PortableUpgradeBannerProps) {
  const theme = useTheme();
  const isCompact = variant === 'compact';

  return (
    <Box
      sx={{
        mb: isCompact ? 0 : 3,
        p: isCompact ? 1.5 : 2,
        borderRadius: 3,
        bgcolor: alpha(theme.palette.info.main, 0.06),
        border: `1px solid ${alpha(theme.palette.info.main, 0.28)}`,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        flexWrap: 'wrap',
        gap: 2,
      }}
    >
      <Stack direction="row" spacing={1.5} alignItems="flex-start" sx={{ flex: 1, minWidth: 200 }}>
        <WorkspacePremiumIcon color="info" sx={{ mt: 0.25 }} />
        <Box>
          <Typography variant="body2" fontWeight={800}>
            {`Upgrade to ${TIER_2_COPY.business.title} (${TIER_2_COPY.business.price})`}
          </Typography>
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.25, lineHeight: 1.5 }}>
            {TIER_2_COPY.business.body}
          </Typography>
        </Box>
      </Stack>
      <Stack direction="row" spacing={1}>
        {onOpenSettings && (
          <Button size="small" variant="outlined" onClick={onOpenSettings} sx={{ fontWeight: 700 }}>
            Settings
          </Button>
        )}
        <Button
          size="small"
          variant="contained"
          color="info"
          href={LICENSING_LINKS.BUSINESS_CHECKOUT}
          target="_blank"
          rel="noopener noreferrer"
          component="a"
          sx={{ fontWeight: 900 }}
        >
          Get Business
        </Button>
      </Stack>
    </Box>
  );
}
