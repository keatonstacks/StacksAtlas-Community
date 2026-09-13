/** Parse hostname from sa-enroll:// connection strings. */
export function parseEnrollmentHost(enrollmentString: string): string | null {
  const trimmed = enrollmentString.trim();
  if (!trimmed) return null;
  try {
    const normalized = trimmed.startsWith('sa-enroll://')
      ? trimmed.replace('sa-enroll://', 'http://')
      : trimmed;
    return new URL(normalized).hostname || null;
  } catch {
    return null;
  }
}

export function isTailscaleEnrollmentHost(host: string): boolean {
  const h = host.toLowerCase();
  return h.includes('.ts.net') || /^100\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(h);
}

export function enrollmentTransportForHost(host: string | null): 'lan' | 'tailscale' | null {
  if (!host) return null;
  return isTailscaleEnrollmentHost(host) ? 'tailscale' : 'lan';
}
