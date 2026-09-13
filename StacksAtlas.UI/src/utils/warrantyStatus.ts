export type WarrantyStatus = 'none' | 'ok' | 'expiring' | 'expired';

export function getWarrantyStatus(warrantyExpiresUtc?: string | null): WarrantyStatus {
  if (!warrantyExpiresUtc) return 'none';
  const expiry = new Date(warrantyExpiresUtc);
  if (Number.isNaN(expiry.getTime())) return 'none';

  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const expiryDay = new Date(expiry);
  expiryDay.setHours(0, 0, 0, 0);

  if (expiryDay < today) return 'expired';

  const soon = new Date(today);
  soon.setDate(soon.getDate() + 90);
  if (expiryDay <= soon) return 'expiring';

  return 'ok';
}

export function formatWarrantyDate(warrantyExpiresUtc?: string | null): string {
  if (!warrantyExpiresUtc) return '';
  const date = new Date(warrantyExpiresUtc);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleDateString();
}
