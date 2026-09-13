/** Release channel for signed manifest lookup (matches API query param). */
export type UpdateChannel = 'stable' | 'preview';

/** Machine-readable update check outcome (matches API). */
export type UpdateCheckStatus = 'upToDate' | 'updateAvailable' | 'unavailable' | 'checkFailed';

/** Response from GET /api/system/version */
export interface ApplianceVersionInfo {
  version: string;
  channel: string;
  installMode: string;
  isPortable: boolean;
  artifactKey: string;
  reportedUtc: string;
}

/** Response from POST /api/system/updates/check */
export interface UpdateCheckResult {
  status: UpdateCheckStatus;
  updateAvailable: boolean;
  currentVersion: string;
  channel: string;
  artifactKey: string;
  availableVersion: string | null;
  downloadUrl: string | null;
  sha256: string | null;
  image: string | null;
  digest: string | null;
  criticality: string | null;
  releaseNotesUrl: string | null;
  publishedUtc: string | null;
  message: string;
  applySupported: boolean;
  applyMode: string | null;
}

export type UpdateApplyStatus = 'applying' | 'failed';

/** Response from POST /api/system/updates/apply */
export interface UpdateApplyResult {
  status: UpdateApplyStatus;
  message: string;
  targetVersion: string | null;
  channel: string | null;
  snapshotApplianceFileName: string | null;
  snapshotFleetFileName: string | null;
  restartRequired: boolean;
}

/** Response from GET /api/system/updates/depot/status (Hub-only). */
export interface UpdateDepotArtifactEntry {
  fileName: string;
  sizeBytes: number;
}

export interface UpdateDepotVersionEntry {
  version: string;
  path: string;
  hasManifest: boolean;
  hasSignature: boolean;
  publishedUtc: string | null;
  artifacts: UpdateDepotArtifactEntry[];
  totalBytes: number;
}

export interface UpdateDepotChannelStatus {
  channel: string;
  versions: UpdateDepotVersionEntry[];
}

export interface UpdateDepotStatus {
  depotRoot: string;
  channels: UpdateDepotChannelStatus[];
  queriedUtc: string;
}

/** Response from POST /api/system/updates/stage (Hub-only). */
export interface UpdateDepotStageResult {
  success: boolean;
  channel: string;
  version: string | null;
  message: string;
  path: string | null;
  stagedArtifactKeys: string[];
  alreadyStaged?: boolean;
  completedUtc: string;
}

/** Appliance-local scheduled apply (GET/PUT /api/system/updates/schedule). */
export interface UpdateScheduleDto {
  enabled: boolean;
  dayOfWeek: number;
  hour: number;
  minute: number;
  lastAppliedVersion: string | null;
}

/** Hub-pushed update offer (GET /api/system/updates/pending-hub). */
export interface PendingHubUpdateDto {
  active: boolean;
  channel: string | null;
  availableVersion: string | null;
  initiatedBy: string | null;
  notifiedUtc: string | null;
  requestedApply: boolean;
  message: string | null;
}
