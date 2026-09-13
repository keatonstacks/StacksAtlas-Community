/** Display labels for device types (spaces between words; searchable in Autocomplete). */

export const DEVICE_TYPE_OPTIONS: string[] = [
  // General / IT
  'Unknown',
  'Computer',
  'Laptop',
  'Workstation',
  'Server',
  'Virtual Machine',
  'Mobile',
  'Tablet',
  'Phone VoIP',
  'IoT',
  'Printer',
  'Storage NAS',
  'Gaming Console',
  'Custom',

  // Networking
  'Network Infrastructure',
  'Network Router',
  'Network Switch',
  'Network AP',
  'Network Gateway',
  'Network Controller',
  'Network Firewall',

  // Audio / Pro-AV
  'Audio Endpoint',
  'Audio DSP',
  'Audio Microphone',
  'Audio Speaker',
  'Audio Soundbar',
  'Audio Amplifier',
  'Audio AV Receiver',
  'Audio Console',
  'Audio Interface',
  'Audio Dante Device',

  // Video & Display
  'Video Display',
  'Video Projector',
  'Video Camera',
  'Video PTZ',
  'Video Switcher',
  'Video Matrix',
  'Video Encoder',
  'Video Decoder',
  'Video NVR',
  'Video Wall Controller',
  'Digital Signage',
  'Media Player',
  'Streaming Device',

  // Control & Collaboration
  'Control Processor',
  'Control Touch Panel',
  'Collaboration Room System',
  'Collaboration Codec',
  'Lighting Controller',

  // Power
  'Power UPS',
  'Power PDU',
];

/** Split camelCase / PascalCase tokens after underscore→space conversion. */
export function formatDeviceTypeLabel(type: string | null | undefined): string {
  if (!type?.trim()) return 'Unknown';
  return type
    .replace(/_/g, ' ')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .replace(/\s+/g, ' ')
    .trim();
}

/** Category prefix for grouping in Autocomplete (e.g. "Audio", "Video", "Network"). */
export function deviceTypeCategory(type: string): string {
  const label = formatDeviceTypeLabel(type);
  const first = label.split(' ')[0] ?? 'Other';
  const general = new Set([
    'Unknown',
    'Computer',
    'Laptop',
    'Workstation',
    'Server',
    'Virtual',
    'Mobile',
    'Tablet',
    'Phone',
    'IoT',
    'Printer',
    'Storage',
    'Gaming',
    'Custom',
    'Media',
    'Streaming',
    'Digital',
    'Lighting',
    'Power',
  ]);
  if (general.has(first)) return 'General';
  return first;
}

/** Case-insensitive filter for Autocomplete (matches any word in the label). */
export function filterDeviceTypeOptions(options: string[], input: string): string[] {
  const q = input.trim().toLowerCase();
  if (!q) return options;
  return options.filter((opt) => {
    const label = formatDeviceTypeLabel(opt).toLowerCase();
    if (label.includes(q)) return true;
    return label.split(/\s+/).some((word) => word.startsWith(q));
  });
}
