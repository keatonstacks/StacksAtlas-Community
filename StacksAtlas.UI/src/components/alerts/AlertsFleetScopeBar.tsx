import { Box, Chip, Stack, Typography } from '@mui/material';
import HubIcon from '@mui/icons-material/Hub';
import { FilterScopeToolbar, type FilterState } from '../FilterScopeToolbar';
import { useGlobalStats } from '../../context/GlobalStatsContext';

interface AlertsFleetScopeBarProps {
  filters: FilterState;
  onFilterChange: (filters: FilterState) => void;
  scopeActive: boolean;
}

export function AlertsFleetScopeBar({ filters, onFilterChange, scopeActive }: AlertsFleetScopeBarProps) {
  const { isHub } = useGlobalStats();

  if (!isHub) return null;

  return (
    <Box sx={{ mb: 3 }}>
      <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 1.5 }}>
        <HubIcon sx={{ fontSize: 18, color: 'secondary.main' }} />
        <Typography variant="body2" fontWeight={800} color="text.secondary" sx={{ letterSpacing: 0.5 }}>
          FLEET SCOPE
        </Typography>
        {scopeActive && (
          <Chip
            label="FILTERED"
            size="small"
            color="secondary"
            sx={{ height: 20, fontSize: '0.6rem', fontWeight: 900 }}
          />
        )}
      </Stack>
      <FilterScopeToolbar initialState={filters} onFilterChange={onFilterChange} />
    </Box>
  );
}
