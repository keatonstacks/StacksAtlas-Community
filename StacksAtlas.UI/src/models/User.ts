export interface User {
    id: string;
    username: string;
    role: string;
    email?: string;
    provider?: string;
    createdAt: string;
    lastLoginAt?: string;
    isActive: boolean;
    // Alert Preferences
    alertEmail?: string;
    alertsEnabled: boolean;
    webhookEnabled: boolean;
    preferredWebhookId: string | null;
    preferredWebhookIds: string[];
    alertOnDeviceDown: boolean;
    alertOnDeviceUp: boolean;
    alertOnNewDevice: boolean;
    alertSeverity: string;
    systemWideWebhooksActive?: boolean;
    originNodeId?: string;
}
