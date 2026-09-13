import { buildNodeHttpUrl, buildNodeHttpsUrl, resolveNodeHttpPort, resolveNodeHttpsPort } from './appliancePorts';

export type FederatedNodeStatusTone = 'success' | 'warning' | 'error' | 'info';

export interface FederatedNodeStatusPresentation {
  label: string;
  tone: FederatedNodeStatusTone;
  isOperational: boolean;
}

export function getFederatedNodeStatusPresentation(status?: string): FederatedNodeStatusPresentation {
  const normalized = status?.toLowerCase() ?? 'offline';

  switch (normalized) {
    case 'online':
      return { label: 'ONLINE', tone: 'success', isOperational: true };
    case 'setup_required':
      return { label: 'SETUP REQUIRED', tone: 'warning', isOperational: false };
    case 'resetting':
      return { label: 'RESETTING', tone: 'info', isOperational: false };
    case 'offline':
      return { label: 'OFFLINE', tone: 'error', isOperational: false };
    default:
      return { label: status?.toUpperCase() ?? 'OFFLINE', tone: 'error', isOperational: false };
  }
}

export function getFederatedNodeStatusColor(theme: { palette: { success: { main: string }; warning: { main: string }; info: { main: string }; error: { main: string } } }, tone: FederatedNodeStatusTone): string {
  switch (tone) {
    case 'success':
      return theme.palette.success.main;
    case 'warning':
      return theme.palette.warning.main;
    case 'info':
      return theme.palette.info.main;
    default:
      return theme.palette.error.main;
  }
}

export function getFederatedNodeSetupUrl(ipAddress?: string, httpsPort?: number): string | null {
  return buildNodeHttpsUrl(ipAddress, httpsPort, '/onboarding');
}

/** Opens the Node appliance dashboard (HTTP; macOS uses 5050, Linux/Docker 5000). */
export function getFederatedNodeOpenUrl(
  ipAddress?: string,
  httpPort?: number,
  _httpsPort?: number,
  osDescription?: string | null
): string | null {
  return buildNodeHttpUrl(ipAddress, httpPort, osDescription, '/');
}

export { resolveNodeHttpPort, resolveNodeHttpsPort };
