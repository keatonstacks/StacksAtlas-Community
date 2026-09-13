import React from 'react';
import { Box, Paper, Typography, alpha, useTheme } from '@mui/material';

export interface KpiCardProps {
  icon: React.ReactElement;
  label: string;
  value: string;
  subValue: string;
  color: string;
  gradient: string;
}

export function KpiCard({ icon, label, value, subValue, color, gradient }: KpiCardProps) {
  const theme = useTheme();
  return (
    <Paper
      variant="outlined"
      sx={{
        p: 2.5,
        borderRadius: 4,
        bgcolor: alpha(theme.palette.background.paper, 0.6),
        backdropFilter: 'blur(10px)',
        border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
        background: gradient,
        position: 'relative',
        overflow: 'hidden',
        transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
        '&:hover': {
          transform: 'translateY(-4px)',
          borderColor: alpha(color, 0.5),
          boxShadow: `0 8px 24px ${alpha(color, 0.15)}`,
          '& .kpi-icon': {
            transform: 'scale(1.1) rotate(5deg)',
            opacity: 0.1,
          },
        },
      }}
    >
      <Box
        className="kpi-icon"
        sx={{
          position: 'absolute',
          right: -8,
          top: -8,
          opacity: 0.05,
          color,
          transition: 'all 0.3s ease',
        }}
      >
        {React.cloneElement(icon, { sx: { fontSize: 80 } } as object)}
      </Box>
      <Typography variant="caption" fontWeight={900} color="text.secondary" sx={{ letterSpacing: 1.5, opacity: 0.7 }}>
        {label}
      </Typography>
      <Typography variant="h4" fontWeight={900} sx={{ my: 1, color, letterSpacing: -1 }}>
        {value}
      </Typography>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <Box sx={{ width: 4, height: 4, borderRadius: '50%', bgcolor: color }} />
        <Typography variant="caption" fontWeight={800} color="text.secondary" sx={{ fontSize: '0.65rem' }}>
          {subValue}
        </Typography>
      </Box>
    </Paper>
  );
}
