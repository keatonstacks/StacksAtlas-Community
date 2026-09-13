import { Grid, Paper, Typography } from '@mui/material';

interface MobileKpiItem {
  label: string;
  value: string;
  color?: string;
}

interface MobileKpiStripProps {
  items: MobileKpiItem[];
}

export function MobileKpiStrip({ items }: MobileKpiStripProps) {
  return (
    <Grid container spacing={1.5} sx={{ mb: 1 }}>
      {items.map((item) => (
        <Grid item xs={6} key={item.label}>
          <Paper
            variant="outlined"
            sx={{
              p: 1.5,
              borderRadius: 2,
              textAlign: 'center',
            }}
          >
            <Typography variant="caption" color="text.secondary" fontWeight={800} display="block">
              {item.label}
            </Typography>
            <Typography variant="h6" fontWeight={900} sx={{ color: item.color ?? 'text.primary', lineHeight: 1.2 }}>
              {item.value}
            </Typography>
          </Paper>
        </Grid>
      ))}
    </Grid>
  );
}
