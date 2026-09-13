export interface SiteResetRecoveryContext {
  hubInitiated: boolean;
  initiatedByUsername?: string | null;
  initiatedAtUtc?: string | null;
  hubDisplayName?: string | null;
}

export interface OnboardingStatus {
  isAdminConfigured: boolean;
  isFoundationComplete: boolean;
  expressBootstrapPending: boolean;
  onboardingNetworkConfirmed: boolean;
  siteName: string;
  legacyMigrationApplied: boolean;
  licenseActive: boolean;
  licenseTier: string;
  allowsHub: boolean;
  allowsFederationJoin: boolean;
  maxStandaloneActivations: number;
  nodeLimit: number;
  hardwareId: string;
  licenseMessage: string | null;
  isEnrolledNode: boolean;
  pendingSiteResetRecovery?: SiteResetRecoveryContext | null;
  isPortable: boolean;
}

export type OnboardingStepId =
  | 'secure-connection'
  | 'welcome'
  | 'admin'
  | 'license'
  | 'plan'
  | 'site'
  | 'network'
  | 'role'
  | 'site-reset'
  | 'done';

export function resolveOnboardingStep(status: OnboardingStatus | null): OnboardingStepId {
  if (!status) return 'welcome';

  if (status.isPortable) {
    if (status.isFoundationComplete) return 'done';
    if (!status.onboardingNetworkConfirmed) return 'welcome';
    return 'network';
  }

  if (!status.isAdminConfigured) return 'welcome';

  const hubSiteResetRecovery =
    status.pendingSiteResetRecovery?.hubInitiated === true && status.isEnrolledNode;

  if (hubSiteResetRecovery && !status.isFoundationComplete) {
    return 'site-reset';
  }

  const expressWizard = status.expressBootstrapPending
    || (!status.isFoundationComplete && !status.legacyMigrationApplied);

  if (expressWizard) {
    if (!status.licenseActive) return 'license';
    if (!status.siteName?.trim()) return 'plan';
    if (status.allowsHub && !status.isPortable && !status.onboardingNetworkConfirmed) return 'role';
    if (!status.onboardingNetworkConfirmed) return 'network';
    return 'done';
  }

  if (!status.licenseActive) return 'license';
  if (!status.siteName?.trim()) return 'plan';
  if (!status.isFoundationComplete) {
    if (status.allowsHub && !status.isPortable && !status.onboardingNetworkConfirmed) return 'role';
    if (!status.onboardingNetworkConfirmed) return 'network';
  }
  return 'done';
}

export const EXPRESS_STEPS: { id: OnboardingStepId; label: string }[] = [
  { id: 'welcome', label: 'Welcome' },
  { id: 'admin', label: 'Administrator' },
  { id: 'license', label: 'License' },
  { id: 'plan', label: 'Your plan' },
  { id: 'site', label: 'Site name' },
  { id: 'network', label: 'Network' },
  { id: 'role', label: 'Role' },
  { id: 'site-reset', label: 'Site recovery' },
  { id: 'done', label: 'Done' },
];

export function stepsForStatus(status: OnboardingStatus | null): { id: OnboardingStepId; label: string }[] {
  if (status?.isPortable) {
    return EXPRESS_STEPS.filter(s => ['welcome', 'network', 'done'].includes(s.id));
  }

  const hubSiteResetRecovery =
    status?.pendingSiteResetRecovery?.hubInitiated === true && status?.isEnrolledNode;

  if (hubSiteResetRecovery) {
    return EXPRESS_STEPS.filter(s => ['welcome', 'admin', 'site-reset', 'done'].includes(s.id));
  }

  return EXPRESS_STEPS;
}

export function stepIndex(id: OnboardingStepId, status?: OnboardingStatus | null): number {
  const steps = status ? stepsForStatus(status) : EXPRESS_STEPS;
  return steps.findIndex(s => s.id === id);
}

export const TLS_ONBOARDING_DISMISSED_KEY = 'stacksatlas.onboarding.secureConnectionDismissed';

/** Show TLS step when cert is not trusted and user has not chosen Skip for now. */
export function shouldShowSecureConnectionStep(certAlreadyTrusted = false): boolean {
  if (certAlreadyTrusted) return false;
  return localStorage.getItem(TLS_ONBOARDING_DISMISSED_KEY) !== '1';
}

/** Remember explicit Skip  -  user can still re-trust later from Settings. */
export function dismissSecureConnectionStep(): void {
  localStorage.setItem(TLS_ONBOARDING_DISMISSED_KEY, '1');
}
