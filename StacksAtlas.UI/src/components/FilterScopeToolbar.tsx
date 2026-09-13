import React, { useState, useEffect } from 'react';
import { 
  Box, 
  Select, 
  MenuItem, 
  FormControl, 
  Checkbox, 
  ListItemText, 
  Typography, 
  IconButton, 
  Popover, 
  TextField, 
  Stack, 
  Button, 
  Divider,
} from '@mui/material';
import FilterAltIcon from '@mui/icons-material/FilterAlt';
import CloseIcon from '@mui/icons-material/Close';
import { ApiService } from '../services/apiService';
import { useGlobalStats } from '../context/GlobalStatsContext';

export interface FilterState {
  nodeIds: string[];
  client: string;
  building: string;
  room: string;
}

interface FilterScopeToolbarProps {
  onFilterChange: (filters: FilterState) => void;
  initialState?: FilterState;
}

export const FilterScopeToolbar: React.FC<FilterScopeToolbarProps> = ({ onFilterChange, initialState }) => {
  const { isHub } = useGlobalStats();
  
  const [nodes, setNodes] = useState<any[]>([]);
  const [selectedNodes, setSelectedNodes] = useState<string[]>(initialState?.nodeIds || []);
  
  const [client, setClient] = useState(initialState?.client || '');
  const [building, setBuilding] = useState(initialState?.building || '');
  const [room, setRoom] = useState(initialState?.room || '');
  
  const [anchorEl, setAnchorEl] = useState<HTMLButtonElement | null>(null);

  useEffect(() => {
    if (isHub) {
      ApiService.getFederationNodes().then(data => {
        if (Array.isArray(data)) setNodes(data);
      }).catch(() => {});
    }
  }, [isHub]);

  useEffect(() => {
    if (!initialState) return;
    setSelectedNodes(initialState.nodeIds || []);
    setClient(initialState.client || '');
    setBuilding(initialState.building || '');
    setRoom(initialState.room || '');
  }, [
    initialState?.nodeIds?.join(','),
    initialState?.client,
    initialState?.building,
    initialState?.room,
  ]);

  // When filters change, debounce and notify parent
  useEffect(() => {
    const timer = setTimeout(() => {
      onFilterChange({
        nodeIds: selectedNodes,
        client: client,
        building: building,
        room: room
      });
    }, 500);
    return () => clearTimeout(timer);
  }, [selectedNodes, client, building, room]);

  const handleNodeChange = (event: any) => {
    const value = event.target.value;
    setSelectedNodes(typeof value === 'string' ? value.split(',') : value);
  };

  const handlePopoverOpen = (event: React.MouseEvent<HTMLButtonElement>) => {
    setAnchorEl(event.currentTarget);
  };

  const handlePopoverClose = () => {
    setAnchorEl(null);
  };

  const open = Boolean(anchorEl);
  const popoverId = open ? 'advanced-filters-popover' : undefined;

  const hasAdvancedFilters = Boolean(client || building || room);

  if (!isHub) return null;

  return (
    <Box sx={{ 
      display: 'flex', 
      alignItems: 'center', 
      gap: 2, 
      mb: 3, 
      p: 1.5, 
      bgcolor: 'background.paper', 
      borderRadius: 2,
      border: '1px solid',
      borderColor: 'divider'
    }}>
      <Box sx={{ display: 'flex', alignItems: 'center' }}>
        <FilterAltIcon sx={{ color: 'text.secondary', mr: 1, fontSize: 20 }} />
        <Typography variant="body2" fontWeight={800} color="text.secondary" sx={{ letterSpacing: 0.5 }}>
          SCOPE:
        </Typography>
      </Box>

      <FormControl sx={{ minWidth: 250, maxWidth: 400 }} size="small">
        <Select
          multiple
          displayEmpty
          value={selectedNodes}
          onChange={handleNodeChange}
          renderValue={(selected) => {
            if (selected.length === 0) {
              return <Typography variant="body2" color="text.secondary">All Nodes</Typography>;
            }
            if (selected.length === nodes.length && nodes.length > 0) {
              return <Typography variant="body2" fontWeight={600}>All Nodes Selected</Typography>;
            }
            return <Typography variant="body2" fontWeight={600}>{selected.length} Node{selected.length > 1 ? 's' : ''} Selected</Typography>;
          }}
          sx={{
            bgcolor: 'background.default',
            borderRadius: 1.5,
            '& .MuiSelect-select': { py: 0.75 }
          }}
        >
          {nodes.map((node) => (
            <MenuItem key={node.id} value={node.id}>
              <Checkbox checked={selectedNodes.indexOf(node.id) > -1} size="small" />
              <ListItemText 
                primary={node.name.toUpperCase()} 
                secondary={node.id} 
                primaryTypographyProps={{ variant: 'body2', fontWeight: 600 }}
                secondaryTypographyProps={{ variant: 'caption', fontSize: '0.65rem' }}
              />
            </MenuItem>
          ))}
        </Select>
      </FormControl>

      <Button 
        variant={hasAdvancedFilters ? "contained" : "outlined"} 
        color={hasAdvancedFilters ? "primary" : "inherit"}
        onClick={handlePopoverOpen}
        sx={{ borderRadius: 1.5, textTransform: 'none', fontWeight: 600, py: 0.5 }}
        size="small"
      >
        {hasAdvancedFilters ? "Advanced Filters Active" : "Advanced..."}
      </Button>

      {/* Clear All Button */}
      {(selectedNodes.length > 0 || hasAdvancedFilters) && (
        <IconButton 
          size="small" 
          onClick={() => {
            setSelectedNodes([]);
            setClient('');
            setBuilding('');
            setRoom('');
          }}
          sx={{ color: 'text.secondary' }}
        >
          <CloseIcon fontSize="small" />
        </IconButton>
      )}

      <Popover
        id={popoverId}
        open={open}
        anchorEl={anchorEl}
        onClose={handlePopoverClose}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        transformOrigin={{ vertical: 'top', horizontal: 'left' }}
        PaperProps={{
          sx: { p: 3, width: 320, mt: 1, borderRadius: 2, border: '1px solid', borderColor: 'divider' }
        }}
      >
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography variant="subtitle2" fontWeight={800}>ADVANCED FILTERS</Typography>
          <IconButton size="small" onClick={handlePopoverClose}><CloseIcon fontSize="small" /></IconButton>
        </Box>
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 3 }}>
          Filter down to specific locations or organizations.
        </Typography>

        <Stack spacing={2.5}>
          <TextField 
            label="Client / Organization" 
            size="small" 
            fullWidth 
            value={client} 
            onChange={(e) => setClient(e.target.value)} 
          />
          <TextField 
            label="Building" 
            size="small" 
            fullWidth 
            value={building} 
            onChange={(e) => setBuilding(e.target.value)} 
          />
          <TextField 
            label="Room / MDF" 
            size="small" 
            fullWidth 
            value={room} 
            onChange={(e) => setRoom(e.target.value)} 
          />
        </Stack>

        <Divider sx={{ my: 2 }} />

        <Button 
          variant="outlined" 
          fullWidth 
          onClick={() => {
            setClient('');
            setBuilding('');
            setRoom('');
          }}
        >
          CLEAR ADVANCED
        </Button>
      </Popover>
    </Box>
  );
};
