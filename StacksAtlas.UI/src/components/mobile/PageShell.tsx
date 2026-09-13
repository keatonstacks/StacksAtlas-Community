import { Box, type SxProps, type Theme } from '@mui/material';
import type { ReactNode } from 'react';

export interface PageShellProps {
  children: ReactNode;
  isMobile: boolean;
  /** Bottom padding  -  defaults to 10 mobile / 5 desktop */
  pb?: number;
  sx?: SxProps<Theme>;
}

/** Standard appliance page wrapper  -  consistent padding and overflow for mobile vs desktop. */
export function PageShell({ children, isMobile, pb, sx }: PageShellProps) {
  return (
    <Box
      sx={{
        pt: 2,
        pb: pb ?? (isMobile ? 10 : 5),
        px: isMobile ? 2 : { xs: 2, md: 4 },
        bgcolor: 'background.default',
        minHeight: '100vh',
        overflowX: isMobile ? 'hidden' : undefined,
        width: '100%',
        boxSizing: 'border-box',
        ...sx,
      }}
    >
      {children}
    </Box>
  );
}
