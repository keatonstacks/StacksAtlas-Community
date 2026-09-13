export interface Device {
    id: string;
    name: string;
    ipAddress: string;
    hostname: string | null;
    nodeId: string; // Federation Node ID
    macAddress: string;
    openPorts: number[];
    vendor: string;
    status: string;
    type: string;
    model: string;
    confidenceScore: number;
    location: string;
    client?: string;
    building?: string;
    room?: string;
    firstSeen: string;
    firstDiscoveredUtc?: string;
    lastSeen: string;
    operatingSystem?: string; // Legacy Lifeboat Support
    discoveryScopeId?: string;
    discoveryInterfaceId?: string;
    discoveryInterfaceName?: string;
    discoveryVlanTag?: string;

    // --- Stability Metrics ---
    scanCount: number; // Aliased to totalSweepsSeen in some contexts
    totalSweepsSeen: number;
    totalSweepsOnline: number;
    uptimePercent: number;
    flapCount: number;
    lastStateChangeUtc: string | null;
    lastLatencyMs?: number;
    averageLatencyMs: number | null;
    stabilityScore?: number;
    lastSweepId: string | null;

    managedByUserId: string | null;
    managedByUsername: string | null;
    alertsEnabled: boolean;
    isMasked?: boolean;

    // §7.9 Phase B  -  permanent removal tombstone
    isPermanentlyRemoved?: boolean;
    removedUtc?: string | null;
    removedBy?: string | null;
    removedReason?: string | null;
    lastRediscoveryAttemptUtc?: string | null;
    lastRediscoveryIp?: string | null;
    rediscoveryHitCount?: number;

    // --- Security Auditor ---
    securityGrade?: number; // 0=Green, 1=Yellow, 2=Red
    securityIssues?: string[];
    deepScanIssues?: string[];

    // --- Asset metadata (1.8.6) ---
    serialNumber?: string | null;
    assetTag?: string | null;
    firmwareVersion?: string | null;
    warrantyExpiresUtc?: string | null;
    isSerialNumberManuallySet?: boolean;
    isFirmwareVersionManuallySet?: boolean;
    isAssetTagManuallySet?: boolean;
    isWarrantyExpiresManuallySet?: boolean;
    isHostnameManuallySet?: boolean;
    assetMetadataSource?: number; // 0=Unknown, 1=Manual, 2=OpenAvc

    // --- Network link / port map (1.9) ---
    attachmentKind?: string;
    attachmentPort?: string | null;
    attachmentParentDeviceId?: string | null;
    attachmentParentMac?: string | null;
    attachmentParentIp?: string | null;
    attachmentParentName?: string | null;
    isAttachmentManuallySet?: boolean;
    isDeleted?: boolean;
}

export interface ServiceDetail {
    id: string;
    deviceId: string;
    port: number;
    protocol: string;
    state: string;
    serviceName?: string;
    product?: string;
    version?: string;
    extraInfo?: string;
    scannedAtUtc: string;
}
