/** Mirrors StacksAtlas.Core.Helpers.AppliancePortDefaults for Hub quick links. */
export const STANDARD_HTTP_PORT = 5000;
export const MAC_HTTP_PORT = 5050;
export const STANDARD_HTTPS_PORT = 5001;

export function isMacOsDescription(osDescription?: string | null): boolean {
  if (!osDescription) return false;
  const os = osDescription.toLowerCase();
  return os.includes('darwin') || os.includes('macos') || os.includes('osx') || os.includes('mac os');
}

/** Normalize HTTP port for a remote federated node (macOS may report 5000 while listening on 5050). */
export function resolveNodeHttpPort(httpPort?: number | null, osDescription?: string | null): number {
  const port = httpPort && httpPort > 0 ? httpPort : STANDARD_HTTP_PORT;
  if (isMacOsDescription(osDescription) && port === STANDARD_HTTP_PORT) return MAC_HTTP_PORT;
  return port;
}

export function resolveNodeHttpsPort(httpsPort?: number | null): number {
  return httpsPort && httpsPort > 0 ? httpsPort : STANDARD_HTTPS_PORT;
}

function bracketHost(ipAddress: string): string {
  return ipAddress.includes(':') && !ipAddress.startsWith('[') ? `[${ipAddress}]` : ipAddress;
}

export function buildNodeHttpUrl(
  ipAddress?: string,
  httpPort?: number | null,
  osDescription?: string | null,
  path = '/'
): string | null {
  if (!ipAddress) return null;
  const port = resolveNodeHttpPort(httpPort, osDescription);
  return `http://${bracketHost(ipAddress)}:${port}${path}`;
}

export function buildNodeHttpsUrl(
  ipAddress?: string,
  httpsPort?: number | null,
  path = '/onboarding'
): string | null {
  if (!ipAddress) return null;
  const port = resolveNodeHttpsPort(httpsPort);
  return `https://${bracketHost(ipAddress)}:${port}${path}`;
}
