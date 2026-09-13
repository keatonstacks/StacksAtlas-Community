/** Normalize ExecutionMode from API (numeric or string after enum JSON changes). */
export function normalizeExecutionMode(mode: unknown): 0 | 1 {
  if (mode === 1 || mode === '1') return 1;
  if (typeof mode === 'string' && mode.toLowerCase() === 'hub') return 1;
  return 0;
}

export function isHubMode(mode: unknown): boolean {
  return normalizeExecutionMode(mode) === 1;
}

/** Normalize LicenseTier from API (numeric or string). */
export function normalizeLicenseTier(tier: unknown): 0 | 1 | 2 | 3 {
  if (tier === 3 || tier === '3' || tier === 'Business') return 3;
  if (tier === 2 || tier === '2' || tier === 'Enterprise') return 2;
  if (tier === 1 || tier === '1' || tier === 'Pro') return 1;
  return 0;
}

export function licenseTierLabel(tier: unknown, isActive: boolean): string {
  if (!isActive) return 'FREE';
  const normalized = normalizeLicenseTier(tier);
  if (normalized === 3) return 'BUSINESS';
  if (normalized === 2) return 'ENTERPRISE';
  if (normalized === 1) return 'BUSINESS';
  return 'FREE';
}

/** Human label for a federated site's reported license tier. */
export function nodeLicenseTierLabel(tier?: string, isPortable?: boolean): string {
  if (isPortable) return 'Free / portable';
  if (!tier) return 'Not reported';
  const t = tier.trim();
  if (t === 'Home') return 'Free / unlicensed';
  if (t === 'Pro' || t === 'Business') return 'Business';
  if (t === 'Enterprise') return 'Enterprise';
  return t;
}

export function isPaidFleetSiteTier(tier?: string): boolean {
  if (!tier) return false;
  const t = tier.trim();
  return t === 'Pro' || t === 'Business' || t === 'Enterprise';
}
