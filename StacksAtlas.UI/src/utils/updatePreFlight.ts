import type { ApplianceVersionInfo } from '../models/Updates';

export interface PreUpdateChecklistItem {
  id: string;
  label: string;
  detail?: string;
}

export function buildPreUpdateChecklist(versionInfo: ApplianceVersionInfo | null): PreUpdateChecklistItem[] {
  const isPortable = versionInfo?.isPortable ?? false;

  if (isPortable) {
    return [
      {
        id: 'downtime',
        label: 'Expect ~60 seconds of downtime while the portable launcher restarts.',
      },
      {
        id: 'backup',
        label: 'A launcher backup will be saved as StacksAtlas-Portable.exe.pre-update.bak next to your exe.',
      },
      {
        id: 'data',
        label: 'Your portable data folder is not modified  -  only the launcher binary is replaced.',
      },
      {
        id: 'close',
        label: 'Close other StacksAtlas browser tabs before applying.',
      },
    ];
  }

  return [
    {
      id: 'downtime',
      label: 'Expect ~60-90 seconds of downtime while the Windows service restarts.',
    },
    {
      id: 'snapshot',
      label: 'A pre-update database snapshot will be created automatically before staging the installer.',
    },
    {
      id: 'users',
      label: 'Active discovery scans may be interrupted  -  consider waiting for a sweep to finish.',
    },
    {
      id: 'rollback',
      label: 'If apply fails, your snapshot can be restored from Settings → Database Infrastructure.',
    },
  ];
}

export function releaseNotesPreview(message: string | null | undefined, availableVersion: string | null): string {
  const msg = (message ?? '').trim();
  if (msg.length > 12 && !/^update v/i.test(msg)) {
    return msg.length > 96 ? `${msg.slice(0, 93)}…` : msg;
  }
  if (availableVersion) {
    return `Review what's new in v${availableVersion} before applying.`;
  }
  return 'A new StacksAtlas release is ready to install.';
}
