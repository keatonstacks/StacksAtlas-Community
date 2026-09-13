export interface ReportsSecurityRisk {
  deviceName: string;
  reason: string;
  level: 'Critical' | 'Warning';
  deviceId?: string;
  findingId?: string;
  title?: string;
  description?: string;
  mitigation?: string;
  severity?: string;
}
