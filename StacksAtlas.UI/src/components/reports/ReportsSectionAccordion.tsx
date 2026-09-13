import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Box,
  Typography,
  useTheme,
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import type { ReactNode } from 'react';

interface ReportsSectionAccordionProps {
  id: string;
  title: string;
  icon: ReactNode;
  badge?: string;
  expanded: boolean;
  onExpandedChange: (expanded: boolean) => void;
  children: ReactNode;
}

export function ReportsSectionAccordion({
  id,
  title,
  icon,
  badge,
  expanded,
  onExpandedChange,
  children,
}: ReportsSectionAccordionProps) {
  const theme = useTheme();

  return (
    <Accordion
      expanded={expanded}
      onChange={(_, next) => onExpandedChange(next)}
      disableGutters
      elevation={0}
      sx={{
        borderRadius: '12px !important',
        border: `1px solid ${theme.palette.divider}`,
        bgcolor: 'background.paper',
        '&:before': { display: 'none' },
        mb: 2,
      }}
    >
      <AccordionSummary
        expandIcon={<ExpandMoreIcon />}
        aria-controls={`${id}-content`}
        id={`${id}-header`}
        sx={{ px: 2.5, minHeight: 56 }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flex: 1, minWidth: 0 }}>
          {icon}
          <Typography variant="subtitle1" fontWeight={800} noWrap>
            {title}
          </Typography>
          {badge && (
            <Typography variant="caption" color="text.secondary" fontWeight={700} sx={{ ml: 'auto', pr: 1 }}>
              {badge}
            </Typography>
          )}
        </Box>
      </AccordionSummary>
      <AccordionDetails sx={{ px: { xs: 2, md: 3 }, pt: 0, pb: 3 }}>
        {children}
      </AccordionDetails>
    </Accordion>
  );
}
