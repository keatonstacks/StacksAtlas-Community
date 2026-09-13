/** Pick the best byte count  -  prefer first positive value (API may send 0 for unset legacy fields). */
export function coalesceStorageBytes(...values: unknown[]): number {
  const nums = values.filter((v): v is number => typeof v === 'number' && Number.isFinite(v) && v >= 0);
  const positive = nums.find((v) => v > 0);
  if (positive !== undefined) return positive;
  return nums[0] ?? 0;
}

export function coalesceBool(...values: unknown[]): boolean {
  for (const value of values) {
    if (typeof value === 'boolean') return value;
  }
  return false;
}

export function mergeStorageMetrics(status: Record<string, unknown> | null, health: Record<string, unknown> | null) {
  const applianceBytes = coalesceStorageBytes(
    health?.applianceSizeBytes,
    health?.sizeBytes,
    status?.applianceDatabaseSize,
    status?.ApplianceDatabaseSize,
    status?.databaseSize,
    status?.DatabaseSize,
  );
  const fleetBytes = coalesceStorageBytes(
    health?.fleetSizeBytes,
    status?.fleetDatabaseSize,
    status?.FleetDatabaseSize,
  );
  return {
    applianceBytes,
    fleetBytes,
    hasFleet: coalesceBool(status?.hasFleetDatabase, status?.HasFleetDatabase, health?.hasFleetDatabase),
    isHub: coalesceBool(status?.isHub, status?.IsHub),
    applianceEngine: (status?.applianceEngine as string) ?? (health?.applianceEngine as string) ?? 'LiteDB',
    fleetEngine: (status?.fleetEngine as string) ?? (health?.fleetEngine as string) ?? 'SQLite',
    canConfigureFleetEngine: coalesceBool(status?.canConfigureFleetEngine, status?.CanConfigureFleetEngine),
  };
}
