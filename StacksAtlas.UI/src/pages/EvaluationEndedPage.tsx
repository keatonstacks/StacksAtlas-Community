import React, { useEffect } from 'react';
import { Box, Paper, Typography } from '@mui/material';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';

const EvaluationEndedPage: React.FC = () => {
  useEffect(() => {
    localStorage.clear();
    sessionStorage.clear();

    const timer = window.setTimeout(() => {
      window.close();
    }, 400);

    return () => window.clearTimeout(timer);
  }, []);

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        bgcolor: '#0A1929',
        color: '#e0e0e0',
        px: 2,
        py: 3,
      }}
    >
      <Paper
        elevation={0}
        sx={{
          maxWidth: 340,
          width: '100%',
          p: 2.5,
          textAlign: 'center',
          borderRadius: 1.5,
          bgcolor: '#001E3C',
          border: '1px solid #173A5E',
        }}
      >
        <CheckCircleOutlineIcon sx={{ fontSize: 28, color: '#22c55e', mb: 1 }} />
        <Typography variant="subtitle1" fontWeight={700} gutterBottom sx={{ fontSize: '1rem' }}>
          Portable session closed
        </Typography>
        <Typography variant="body2" color="#94a3b8" sx={{ mb: 1, fontSize: '0.8125rem', lineHeight: 1.45 }}>
          Portable data is being removed and StacksAtlas has shut down.
        </Typography>
        <Typography variant="caption" color="#64748B" sx={{ fontSize: '0.75rem', lineHeight: 1.4 }}>
          Close this tab. On Mac, eject the DMG if you launched from the installer volume.
        </Typography>
      </Paper>
    </Box>
  );
};

export default EvaluationEndedPage;
