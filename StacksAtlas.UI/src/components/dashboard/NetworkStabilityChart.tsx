import {
  Box, Paper, Typography, Chip, Stack, Divider, useTheme,
} from '@mui/material';
import { Radar as RadarIcon, Sensors as SensorsIcon } from '@mui/icons-material';
import {
  AreaChart, Area, XAxis, YAxis, ResponsiveContainer, Tooltip, CartesianGrid, ReferenceLine,
} from 'recharts';

interface SweepPoint {
  start: string;
  totalOnline: number;
}

interface NetworkStabilityChartProps {
  sweeps: SweepPoint[];
  deviceCount: number;
  height?: number;
  compact?: boolean;
  hideHeader?: boolean;
}

export function NetworkStabilityChart({
  sweeps,
  deviceCount,
  height = 300,
  compact = false,
  hideHeader = false,
}: NetworkStabilityChartProps) {
  const theme = useTheme();
  const peak = Math.max(...sweeps.map((s) => s.totalOnline), 0);
  const chartHeight = height - (hideHeader ? 16 : compact ? 72 : 88);

  const chart = (
    <ResponsiveContainer width="100%" height={chartHeight}>
        <AreaChart data={sweeps} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
          <defs>
            <linearGradient id="colorSweepMobile" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor={theme.palette.primary.main} stopOpacity={0.3} />
              <stop offset="95%" stopColor={theme.palette.primary.main} stopOpacity={0} />
            </linearGradient>
          </defs>
          <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={theme.palette.divider} />
          <XAxis dataKey="start" hide />
          <YAxis
            tick={{ fontSize: 10, fill: theme.palette.text.secondary }}
            axisLine={false}
            tickLine={false}
            domain={[0, (dataMax: number) => Math.max(dataMax + 2, deviceCount + 2)]}
          />
          <Tooltip
            cursor={{ stroke: theme.palette.primary.main, strokeWidth: 1 }}
            content={({ active, payload }) => {
              if (active && payload && payload.length) {
                const data = payload[0].payload as SweepPoint;
                const offline = Math.max(0, deviceCount - data.totalOnline);
                return (
                  <Box sx={{
                    bgcolor: 'background.paper',
                    p: 1.5,
                    border: `1px solid ${theme.palette.divider}`,
                    borderRadius: 2,
                    boxShadow: '0 4px 20px rgba(0,0,0,0.4)',
                  }}>
                    <Typography variant="caption" color="primary" sx={{ fontWeight: 900 }}>
                      {new Date(data.start).toLocaleTimeString()}
                    </Typography>
                    <Divider sx={{ my: 1 }} />
                    <Stack spacing={0.5}>
                      <Typography variant="caption" sx={{ display: 'flex', justifyContent: 'space-between', gap: 3 }}>
                        <span>ONLINE:</span> <strong>{data.totalOnline}</strong>
                      </Typography>
                      <Typography variant="caption" sx={{ display: 'flex', justifyContent: 'space-between', color: offline > 0 ? 'error.main' : 'text.secondary' }}>
                        <span>OFFLINE:</span> <strong>{offline}</strong>
                      </Typography>
                    </Stack>
                  </Box>
                );
              }
              return null;
            }}
          />
          <ReferenceLine
            y={deviceCount}
            stroke={theme.palette.success.main}
            strokeDasharray="5 5"
            strokeOpacity={0.5}
          />
          <Area
            type="stepAfter"
            dataKey="totalOnline"
            stroke={theme.palette.primary.main}
            strokeWidth={3}
            fillOpacity={1}
            fill="url(#colorSweepMobile)"
            isAnimationActive={false}
          />
        </AreaChart>
      </ResponsiveContainer>
  );

  if (hideHeader) {
    return <Box sx={{ width: '100%', overflow: 'hidden' }}>{chart}</Box>;
  }

  return (
    <Paper
      variant="outlined"
      sx={{
        p: compact ? 2 : 3,
        bgcolor: 'background.paper',
        borderRadius: 3,
        height,
        position: 'relative',
        overflow: 'hidden',
      }}
    >
      <Stack direction="row" justifyContent="space-between" mb={2} flexWrap="wrap" gap={1}>
        <Box display="flex" alignItems="center" gap={1}>
          <SensorsIcon color="primary" sx={{ fontSize: 18 }} />
          <Typography variant="overline" fontWeight={900} color="text.secondary" sx={{ letterSpacing: 1.5 }}>
            NETWORK STABILITY
          </Typography>
        </Box>
        <Stack direction="row" gap={1}>
          <Chip label={`PEAK: ${peak}`} size="small" sx={{ fontWeight: 700, borderRadius: 1 }} />
          <Chip
            icon={<RadarIcon sx={{ fontSize: '14px !important' }} />}
            label="LIVE"
            size="small"
            color="primary"
            sx={{
              fontWeight: 900,
              borderRadius: 1,
              '@keyframes pulseOpacity': {
                '0%': { opacity: 1 },
                '50%': { opacity: 0.4 },
                '100%': { opacity: 1 },
              },
              animation: 'pulseOpacity 2s infinite ease-in-out',
            }}
          />
        </Stack>
      </Stack>
      {chart}
    </Paper>
  );
}
