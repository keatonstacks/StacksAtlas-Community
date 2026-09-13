import { useState } from 'react';
import { Box, Dialog, IconButton, Stack, Typography } from '@mui/material';
import CloseFullscreenIcon from '@mui/icons-material/CloseFullscreen';
import { TopologyGraphCanvas } from './TopologyGraphCanvas';

export const TopologyGraph = ({
    onNodeClick,
    nodeId,
    selectedNodeId,
}: {
    onNodeClick?: (nodeId: string) => void;
    nodeId?: string;
    selectedNodeId?: string | null;
}) => {
    const [fullscreen, setFullscreen] = useState(false);

    const handleNodeClick = (id: string) => {
        onNodeClick?.(id);
    };

    const handleNodeClickFromFullscreen = (id: string) => {
        setFullscreen(false);
        onNodeClick?.(id);
    };

    return (
        <>
            <Box sx={{ width: '100%', height: '100%' }}>
                <TopologyGraphCanvas
                    onNodeClick={handleNodeClick}
                    nodeId={nodeId}
                    selectedNodeId={selectedNodeId}
                    showFullscreenButton
                    onOpenFullscreen={() => setFullscreen(true)}
                />
            </Box>

            <Dialog fullScreen open={fullscreen} onClose={() => setFullscreen(false)}>
                <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column', bgcolor: 'background.default' }}>
                    <Stack
                        direction="row"
                        justifyContent="space-between"
                        alignItems="center"
                        sx={{ px: 2, py: 1, borderBottom: 1, borderColor: 'divider' }}
                    >
                        <Typography variant="subtitle2" fontWeight={900}>
                            LIVE TOPOLOGY MAP
                        </Typography>
                        <IconButton onClick={() => setFullscreen(false)} aria-label="Close full screen">
                            <CloseFullscreenIcon />
                        </IconButton>
                    </Stack>
                    <Box sx={{ flex: 1, minHeight: 0 }}>
                        <TopologyGraphCanvas
                            onNodeClick={handleNodeClickFromFullscreen}
                            nodeId={nodeId}
                            selectedNodeId={selectedNodeId}
                        />
                    </Box>
                </Box>
            </Dialog>
        </>
    );
};
