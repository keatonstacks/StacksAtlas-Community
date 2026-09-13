/** Shared CSV export formatting helpers. */

export function formatMacForCsv(mac?: string | null): string {
  if (!mac) return "";
  const clean = mac.replace(/[^a-fA-F0-9]/g, "").toUpperCase();
  if (clean.length !== 12) return mac;
  return clean.match(/.{2}/g)!.join(":");
}

export function formatWarrantyDateForCsv(value?: string | null): string {
  if (!value) return "";
  return value.slice(0, 10);
}
