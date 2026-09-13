import {
  Box,
  Divider,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Stack,
  Toolbar,
  Typography,
  alpha,
  Badge,
  useTheme,
} from '@mui/material';
import {
  HelpOutline as HelpOutlineIcon,
  Menu as MenuIcon,
  Close as CloseIcon,
  Terminal as TerminalIcon,
} from '@mui/icons-material';
import { Link } from 'react-router-dom';
import type { ReactNode } from 'react';

export interface AppNavItem {
  label: string;
  path: string;
  icon: ReactNode;
}

interface AppSidebarContentProps {
  isHub: boolean;
  collapsed: boolean;
  isMobile: boolean;
  currentPath: string;
  navItems: AppNavItem[];
  recentAlertCount: number;
  settingsUpdateDot: boolean;
  onToggleCollapsed?: () => void;
  onCloseMobile?: () => void;
}

export function AppSidebarContent({
  isHub,
  collapsed,
  isMobile,
  currentPath,
  navItems,
  recentAlertCount,
  settingsUpdateDot,
  onToggleCollapsed,
  onCloseMobile,
}: AppSidebarContentProps) {
  const theme = useTheme();
  const showLabels = isMobile || !collapsed;

  const handleNavClick = () => {
    if (isMobile) onCloseMobile?.();
  };

  return (
    <>
      <Toolbar sx={{ justifyContent: showLabels ? 'space-between' : 'center', px: 2, py: 1 }}>
        {showLabels && (
          <Stack direction="row" alignItems="center" spacing={1}>
            <TerminalIcon color="primary" sx={{ fontSize: 22 }} />
            <Box>
              <Typography variant="subtitle1" fontWeight={900} sx={{ letterSpacing: 1, lineHeight: 1 }}>
                STACKS<span style={{ color: theme.palette.primary.main }}>ATLAS</span>
              </Typography>
              <Typography variant="caption" sx={{ fontSize: '0.6rem', opacity: 0.6, fontWeight: 700 }}>
                {isHub ? 'FEDERATION HUB' : 'NETWORK ENGINE'}
              </Typography>
            </Box>
          </Stack>
        )}
        {isMobile ? (
          <IconButton onClick={onCloseMobile} size="small" aria-label="Close navigation">
            <CloseIcon fontSize="small" />
          </IconButton>
        ) : (
          <IconButton onClick={onToggleCollapsed} size="small" aria-label="Collapse navigation">
            <MenuIcon fontSize="small" />
          </IconButton>
        )}
      </Toolbar>

      <Divider sx={{ opacity: 0.5 }} />

      <List sx={{ px: 1.5, mt: 2 }}>
        {navItems.map((item) => {
          const isActive = currentPath === item.path;
          return (
            <ListItemButton
              key={item.path}
              component={Link}
              to={item.path}
              onClick={handleNavClick}
              selected={isActive}
              sx={{
                borderRadius: '8px',
                mb: 1,
                minHeight: 48,
                justifyContent: showLabels ? 'flex-start' : 'center',
                px: showLabels ? 2 : 1,
                position: 'relative',
                transition: 'all 0.2s ease',
                '&.Mui-selected': {
                  bgcolor: alpha(theme.palette.primary.main, 0.08),
                  color: 'primary.main',
                  '&::before': {
                    content: '""',
                    position: 'absolute',
                    left: -12,
                    height: '60%',
                    width: 4,
                    bgcolor: 'primary.main',
                    borderRadius: '0 4px 4px 0',
                  },
                  '& .MuiListItemIcon-root': { color: 'primary.main' },
                  '&:hover': { bgcolor: alpha(theme.palette.primary.main, 0.12) },
                },
              }}
            >
              <ListItemIcon sx={{ minWidth: showLabels ? 36 : 0, justifyContent: 'center' }}>
                {item.label === 'Alerts' ? (
                  <Badge badgeContent={recentAlertCount} color="error" variant="dot" invisible={recentAlertCount === 0}>
                    {item.icon}
                  </Badge>
                ) : item.label === 'Settings' && settingsUpdateDot ? (
                  <Badge color="success" variant="dot">
                    {item.icon}
                  </Badge>
                ) : (
                  item.icon
                )}
              </ListItemIcon>
              {showLabels && (
                <ListItemText
                  primary={item.label}
                  primaryTypographyProps={{
                    fontSize: '0.85rem',
                    fontWeight: isActive ? 800 : 600,
                    letterSpacing: 0.5,
                  }}
                />
              )}
            </ListItemButton>
          );
        })}
      </List>

      <List sx={{ px: 1.5, mt: 'auto', mb: 2 }}>
        <ListItemButton
          component="a"
          href="https://stacksatlas.com/docs"
          target="_blank"
          rel="noopener noreferrer"
          onClick={handleNavClick}
          sx={{
            borderRadius: '8px',
            minHeight: 48,
            justifyContent: showLabels ? 'flex-start' : 'center',
            px: showLabels ? 2 : 1,
            opacity: 0.7,
            '&:hover': {
              opacity: 1,
              bgcolor: alpha(theme.palette.primary.main, 0.05),
            },
          }}
        >
          <ListItemIcon sx={{ minWidth: showLabels ? 36 : 0, justifyContent: 'center' }}>
            <HelpOutlineIcon fontSize="small" />
          </ListItemIcon>
          {showLabels && (
            <ListItemText
              primary="Documentation"
              primaryTypographyProps={{
                fontSize: '0.85rem',
                fontWeight: 600,
                letterSpacing: 0.5,
              }}
            />
          )}
        </ListItemButton>
      </List>
    </>
  );
}
