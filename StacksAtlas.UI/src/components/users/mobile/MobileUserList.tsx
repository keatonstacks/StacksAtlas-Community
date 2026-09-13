import { Box, CircularProgress, Stack, Typography } from '@mui/material';
import type { User } from '../../../models/User';
import { MobileUserCard } from './MobileUserCard';

interface MobileUserListProps {
  users: User[];
  loading: boolean;
  isHub: boolean;
  isNode: boolean;
  currentNodeId: string | null;
  currentUsername?: string;
  onOpen: (user: User) => void;
  onReset: (user: User) => void;
  onDelete: (id: string) => void;
}

export function MobileUserList({
  users,
  loading,
  isHub,
  isNode,
  currentNodeId,
  currentUsername,
  onOpen,
  onReset,
  onDelete,
}: MobileUserListProps) {
  if (loading) {
    return (
      <Box sx={{ py: 8, textAlign: 'center' }}>
        <CircularProgress size={32} />
        <Typography sx={{ mt: 2, color: 'text.secondary' }}>Loading users...</Typography>
      </Box>
    );
  }

  if (users.length === 0) {
    return (
      <Typography sx={{ py: 6, textAlign: 'center', color: 'text.secondary' }}>
        No users found matching your search.
      </Typography>
    );
  }

  return (
    <Stack spacing={1.25}>
      {users.map((u) => (
        <MobileUserCard
          key={u.id}
          user={u}
          isHub={isHub}
          isNode={isNode}
          currentNodeId={currentNodeId}
          currentUsername={currentUsername}
          onOpen={() => onOpen(u)}
          onReset={() => onReset(u)}
          onDelete={() => onDelete(u.id)}
        />
      ))}
    </Stack>
  );
}
