import {
  Box,
  Button,
  FormControl,
  Grid,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  TextField,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import RouterIcon from '@mui/icons-material/Router';
import SearchIcon from '@mui/icons-material/Search';
import type { HubFleetFilters } from './types';

interface HubFleetToolbarProps {
  filters: HubFleetFilters;
  clients: string[];
  buildings: string[];
  isAdmin: boolean;
  onFiltersChange: (patch: Partial<HubFleetFilters>) => void;
  onEnrollClick: () => void;
}

export function HubFleetToolbar({
  filters,
  clients,
  buildings,
  isAdmin,
  onFiltersChange,
  onEnrollClick,
}: HubFleetToolbarProps) {
  const theme = useTheme();

  return (
    <Stack spacing={2} mb={3}>
      {/* Title + enroll: never share a row with filter controls */}
      <Stack
        direction="row"
        justifyContent="space-between"
        alignItems="flex-start"
        flexWrap="wrap"
        gap={1.5}
      >
        <Box sx={{ minWidth: 0, flex: '1 1 12rem' }}>
          <Typography variant="subtitle1" fontWeight={900} sx={{ letterSpacing: -0.5, lineHeight: 1.2 }}>
            FLEET INVENTORY
          </Typography>
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{ display: 'block', mt: 0.5, lineHeight: 1.45, maxWidth: '36rem' }}
          >
            Filter by client or building.
            <Box component="span" sx={{ display: { xs: 'block', sm: 'inline' } }}>
              {' '}Expand a row for site details.
            </Box>
          </Typography>
        </Box>

        {isAdmin && (
          <Button
            variant="contained"
            color="secondary"
            size="small"
            startIcon={<RouterIcon />}
            onClick={onEnrollClick}
            sx={{ fontWeight: 800, borderRadius: 2, whiteSpace: 'nowrap', flexShrink: 0 }}
          >
            Enroll node
          </Button>
        )}
      </Stack>

      {/* Filters  -  full width grid so nothing overlaps at ~1080p + sidebar */}
      <Grid container spacing={1.5} alignItems="center">
        <Grid item xs={12} sm={6} md={4}>
          <FormControl size="small" fullWidth>
            <InputLabel>Client</InputLabel>
            <Select
              label="Client"
              value={filters.client}
              onChange={(e) => onFiltersChange({ client: e.target.value })}
            >
              <MenuItem value="">All clients</MenuItem>
              {clients.map((c) => (
                <MenuItem key={c} value={c}>{c}</MenuItem>
              ))}
            </Select>
          </FormControl>
        </Grid>

        <Grid item xs={12} sm={6} md={4}>
          <FormControl size="small" fullWidth>
            <InputLabel>Building</InputLabel>
            <Select
              label="Building"
              value={filters.building}
              onChange={(e) => onFiltersChange({ building: e.target.value })}
            >
              <MenuItem value="">All buildings</MenuItem>
              {buildings.map((b) => (
                <MenuItem key={b} value={b}>{b}</MenuItem>
              ))}
            </Select>
          </FormControl>
        </Grid>

        <Grid item xs={12} md={4}>
          <Box
            sx={{
              bgcolor: alpha(theme.palette.background.default, 0.5),
              px: 2,
              py: 0.75,
              borderRadius: 2.5,
              display: 'flex',
              alignItems: 'center',
              width: '100%',
              border: '1px solid',
              borderColor: alpha(theme.palette.divider, 0.1),
              '&:focus-within': {
                borderColor: 'primary.main',
                bgcolor: alpha(theme.palette.background.default, 0.8),
              },
            }}
          >
            <SearchIcon sx={{ color: 'text.secondary', mr: 1, fontSize: 18, flexShrink: 0 }} />
            <TextField
              placeholder="Search sites…"
              variant="standard"
              fullWidth
              value={filters.search}
              onChange={(e) => onFiltersChange({ search: e.target.value })}
              InputProps={{
                disableUnderline: true,
                sx: { fontSize: '0.75rem', fontWeight: 700 },
              }}
            />
          </Box>
        </Grid>
      </Grid>
    </Stack>
  );
}
