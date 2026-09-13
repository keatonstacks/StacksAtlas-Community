import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Link,
  Stack,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import ChecklistIcon from '@mui/icons-material/Checklist';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import { REMOTE_SITE_CHECKLIST_STEPS } from './tailscaleOperatorGuide';

interface RemoteSiteChecklistProps {
  defaultExpanded?: boolean;
}

export function RemoteSiteChecklist({ defaultExpanded = false }: RemoteSiteChecklistProps) {
  const theme = useTheme();

  return (
    <Accordion
      defaultExpanded={defaultExpanded}
      disableGutters
      elevation={0}
      sx={{
        borderRadius: '12px !important',
        border: `1px solid ${alpha(theme.palette.info.main, 0.25)}`,
        bgcolor: alpha(theme.palette.info.main, 0.04),
        '&:before': { display: 'none' },
      }}
    >
      <AccordionSummary expandIcon={<ExpandMoreIcon />} sx={{ px: 2, minHeight: 48 }}>
        <Stack direction="row" alignItems="center" spacing={1}>
          <ChecklistIcon color="info" sx={{ fontSize: 20 }} />
          <Typography variant="subtitle2" fontWeight={800}>
            Remote site checklist
          </Typography>
          <Typography variant="caption" color="text.secondary" sx={{ ml: 0.5 }}>
            (5 steps)
          </Typography>
        </Stack>
      </AccordionSummary>
      <AccordionDetails sx={{ px: 2, pt: 0, pb: 2 }}>
        <Stack spacing={2}>
          {REMOTE_SITE_CHECKLIST_STEPS.map((step, index) => (
            <Stack key={step.title} spacing={0.5}>
              <Typography variant="body2" fontWeight={800}>
                {index + 1}. {step.title}
              </Typography>
              <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1.55, display: 'block' }}>
                {step.body}
              </Typography>
              {step.href && (
                <Link
                  href={step.href}
                  target="_blank"
                  rel="noopener noreferrer"
                  variant="caption"
                  sx={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: 0.5,
                    fontWeight: 700,
                    width: 'fit-content',
                  }}
                >
                  {step.hrefLabel ?? 'Learn more'}
                  <OpenInNewIcon sx={{ fontSize: 14 }} />
                </Link>
              )}
            </Stack>
          ))}
        </Stack>
      </AccordionDetails>
    </Accordion>
  );
}
