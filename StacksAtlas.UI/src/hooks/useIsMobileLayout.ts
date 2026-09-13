import { useMediaQuery, useTheme } from '@mui/material';

function getClientMatches(query: string): boolean {
  if (typeof window === 'undefined') return false;
  const media = query.startsWith('@media') ? query.replace(/^@media\s*/, '') : query;
  return window.matchMedia(media).matches;
}

/** True below MUI `md` (900px). Desktop layout is unchanged at md and above. */
export function useIsMobileLayout(): boolean {
  const theme = useTheme();
  const query = theme.breakpoints.down('md');
  return useMediaQuery(query, {
    defaultMatches: getClientMatches(query),
  });
}
