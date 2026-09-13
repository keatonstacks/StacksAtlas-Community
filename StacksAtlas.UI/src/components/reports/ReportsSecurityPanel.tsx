import { useMemo, useState } from 'react';
import {
  Box,
  Chip,
  InputAdornment,
  Stack,
  TextField,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';

import { SecurityFindingCard } from '../security/SecurityFindingCard';
import { toParsedSecurityFinding } from '../../utils/securityFindings';
import {
  filterSecurityRisks,
  type ReportsSeverityFilter,
} from '../../utils/reportsSecurityFilter';
import type { ReportsSecurityRisk } from './types';

export type { ReportsSecurityRisk } from './types';

interface ReportsSecurityPanelProps {
  risks: ReportsSecurityRisk[];
  onViewDevice: (deviceId: string) => void;
  onCopied: (message: string) => void;
}

const SEVERITY_OPTIONS: ReportsSeverityFilter[] = ['Critical', 'High', 'Medium'];

export function ReportsSecurityPanel({ risks, onViewDevice, onCopied }: ReportsSecurityPanelProps) {
  const theme = useTheme();
  const [search, setSearch] = useState('');
  const [severities, setSeverities] = useState<ReportsSeverityFilter[]>([]);

  const filteredRisks = useMemo(
    () => filterSecurityRisks(risks, { search, severities }),
    [risks, search, severities]
  );

  const toggleSeverity = (severity: ReportsSeverityFilter) => {
    setSeverities((prev) =>
      prev.includes(severity) ? prev.filter((s) => s !== severity) : [...prev, severity]
    );
  };

  return (
    <Box>
      {risks.length > 0 && (
        <Stack spacing={2} sx={{ mb: 2 }}>
          <TextField
            size="small"
            placeholder="Search findings (device, SA ID, title, description)"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            fullWidth
            InputProps={{
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" color="action" />
                </InputAdornment>
              ),
            }}
            sx={{ maxWidth: { md: 480 } }}
          />
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            {SEVERITY_OPTIONS.map((severity) => (
              <Chip
                key={severity}
                label={severity}
                size="small"
                clickable
                color={severities.includes(severity) ? 'primary' : 'default'}
                variant={severities.includes(severity) ? 'filled' : 'outlined'}
                onClick={() => toggleSeverity(severity)}
                sx={{ fontWeight: 700 }}
              />
            ))}
            {(search || severities.length > 0) && (
              <Typography variant="caption" color="text.secondary" sx={{ alignSelf: 'center', ml: 0.5 }}>
                Showing {filteredRisks.length} of {risks.length}
              </Typography>
            )}
          </Stack>
        </Stack>
      )}

      {risks.length > 0 ? (
        filteredRisks.length > 0 ? (
          <Box
            sx={{
              maxHeight: { xs: '50vh', md: 'min(60vh, 640px)' },
              overflowY: 'auto',
              pr: 0.5,
              mr: -0.5,
            }}
          >
            <Stack spacing={1.5}>
              {filteredRisks.map((risk) => {
                const finding = toParsedSecurityFinding(risk);
                return (
                  <SecurityFindingCard
                    key={`${risk.deviceId}-${finding.raw}`}
                    finding={finding}
                    deviceName={risk.deviceName}
                    deviceId={risk.deviceId}
                    onViewDevice={onViewDevice}
                    onCopied={onCopied}
                  />
                );
              })}
            </Stack>
          </Box>
        ) : (
          <Box
            sx={{
              py: 4,
              textAlign: 'center',
              borderRadius: 2,
              bgcolor: alpha(theme.palette.grey[500], 0.04),
              border: '1px dashed',
              borderColor: alpha(theme.palette.grey[500], 0.2),
            }}
          >
            <Typography variant="body2" color="text.secondary">
              No findings match your search or severity filters.
            </Typography>
          </Box>
        )
      ) : (
        <Box
          sx={{
            py: 4,
            textAlign: 'center',
            borderRadius: 2,
            bgcolor: alpha(theme.palette.success.main, 0.04),
            border: '1px dashed',
            borderColor: alpha(theme.palette.success.main, 0.2),
          }}
        >
          <Typography variant="body2" color="text.secondary">
            Estate is clean. No hygiene risks detected.
          </Typography>
        </Box>
      )}

      {risks.length > 0 && (
        <Typography variant="caption" color="text.disabled" sx={{ display: 'block', mt: 2 }}>
          Select text to copy details, or use the copy icon. Open device with the arrow. Rows are not fully clickable.
        </Typography>
      )}
    </Box>
  );
}
