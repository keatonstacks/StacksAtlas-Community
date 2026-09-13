import {
  Box,
  Collapse,
  CircularProgress,
  Divider,
  LinearProgress,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import ManageSearchIcon from '@mui/icons-material/ManageSearch';
import KeyboardArrowDownIcon from '@mui/icons-material/KeyboardArrowDown';
import KeyboardArrowUpIcon from '@mui/icons-material/KeyboardArrowUp';
import type { ServiceDetail } from '../../models/Device';

interface DeepScanPanelProps {
  serviceDetails: ServiceDetail[];
  isDeepScanning: boolean;
  scanProgress: number;
  isOpen: boolean;
  isHub?: boolean;
  onToggle: () => void;
}

export function DeepScanPanel({
  serviceDetails,
  isDeepScanning,
  scanProgress,
  isOpen,
  isHub = false,
  onToggle,
}: DeepScanPanelProps) {
  const theme = useTheme();

  if (serviceDetails.length === 0 && !isDeepScanning && !isHub) return null;

  return (
    <Box
      sx={{
        mt: 2,
        bgcolor: alpha(theme.palette.primary.main, 0.03),
        borderRadius: 2,
        overflow: 'hidden',
        border: '1px solid',
        borderColor: 'divider',
      }}
    >
      <Box
        onClick={onToggle}
        sx={{
          p: 1.5,
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          cursor: 'pointer',
          bgcolor: isOpen ? alpha(theme.palette.primary.main, 0.08) : 'transparent',
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <ManageSearchIcon color="primary" sx={{ fontSize: 20 }} />
          <Typography variant="subtitle2" fontWeight={700} color="primary.main">
            Network intelligence
          </Typography>
          {isDeepScanning && (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, ml: 1 }}>
              <CircularProgress size={12} color="primary" />
              <Typography variant="caption" fontWeight={700} color="primary">
                {scanProgress}%
              </Typography>
            </Box>
          )}
        </Box>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography variant="caption" color="text.secondary" fontWeight={600}>
            {serviceDetails.length} services
          </Typography>
          {isOpen ? <KeyboardArrowUpIcon fontSize="small" /> : <KeyboardArrowDownIcon fontSize="small" />}
        </Box>
      </Box>

      <Collapse in={isOpen}>
        <Divider />
        {isDeepScanning && (
          <LinearProgress variant="determinate" value={scanProgress} sx={{ height: 4 }} />
        )}
        <TableContainer sx={{ maxHeight: 280 }}>
          <Table size="small" stickyHeader>
            <TableHead>
              <TableRow>
                <TableCell sx={{ fontWeight: 800, fontSize: '0.7rem', py: 1 }}>Port</TableCell>
                <TableCell sx={{ fontWeight: 800, fontSize: '0.7rem', py: 1 }}>Service</TableCell>
                <TableCell sx={{ fontWeight: 800, fontSize: '0.7rem', py: 1 }}>Version</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {serviceDetails.map((svc) => (
                <TableRow key={`${svc.port}-${svc.protocol}`} hover>
                  <TableCell sx={{ fontFamily: 'monospace', fontWeight: 700, fontSize: '0.75rem', color: 'primary.main' }}>
                    {svc.port}/{svc.protocol}
                  </TableCell>
                  <TableCell sx={{ fontSize: '0.75rem', fontWeight: 600 }}>
                    {svc.product || svc.serviceName || 'Unknown'}
                  </TableCell>
                  <TableCell sx={{ fontSize: '0.7rem', color: 'text.secondary', fontFamily: 'monospace' }}>
                    {svc.version || ' - '}
                  </TableCell>
                </TableRow>
              ))}
              {serviceDetails.length === 0 && isDeepScanning && (
                <TableRow>
                  <TableCell colSpan={3} align="center" sx={{ py: 3 }}>
                    <Typography variant="caption" color="text.secondary">
                      {isHub ? `Analyzing services on remote node… (${scanProgress}%)` : `Analyzing services… (${scanProgress}%)`}
                    </Typography>
                  </TableCell>
                </TableRow>
              )}
              {serviceDetails.length === 0 && !isDeepScanning && isHub && (
                <TableRow>
                  <TableCell colSpan={3} align="center" sx={{ py: 3 }}>
                    <Typography variant="caption" color="text.secondary" display="block">
                      No service fingerprints yet. Run Deep Scan  -  results are stored on the enrolled node and streamed here when complete.
                    </Typography>
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      </Collapse>
    </Box>
  );
}
