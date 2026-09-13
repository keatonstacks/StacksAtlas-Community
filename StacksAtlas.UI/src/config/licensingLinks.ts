/** Sovereign checkout URLs. 2-tier GTM at 1.8 (Free portable + Business). */
export const LICENSING_LINKS = {
  BUSINESS_CHECKOUT:
    'https://stacksatlas.lemonsqueezy.com/checkout/buy/61266882-f3c5-4837-a569-029f972ce7f0',
  DOCS_PRICING: 'https://stacksatlas.com/pricing',
  DOCS_INSTALL: 'https://stacksatlas.com/docs/installation',
  SITE_DOWNLOAD: 'https://stacksatlas.com/#download',
} as const;

/** Signed stable-channel builds on releases.stacksatlas.com */
export const DOWNLOAD_LINKS = {
  WIN_PORTABLE: 'https://releases.stacksatlas.com/stable/win-x64-portable',
  MAC_DMG: 'https://releases.stacksatlas.com/stable/osx-universal-dmg',
  WIN_MSI: 'https://releases.stacksatlas.com/stable/win-x64-msi',
  /** Docker image tar (`docker load -i`); GHCR `:latest` remains the online pull path. */
  LINUX_DOCKER: 'https://releases.stacksatlas.com/stable/linux-docker',
} as const;

export const TIER_2_COPY = {
  free: {
    title: 'Free (Portable)',
    body: 'Session scanner on Win/Mac/Docker, no license key. Unlimited device discovery; session data clears when you exit portable mode.',
    price: '$0',
    highlights: [
      'Unlimited discovery, no device cap',
      'Win, Mac, or Docker portable',
      'Session ends when you close the app',
    ],
  },
  business: {
    title: 'Business',
    body: 'Always-on appliance: background service, persistent history, federation, Hub fleet, and SSO. Discovery is unlimited on every tier.',
    price: '$40',
    highlights: [
      'Persistent database & history',
      'Background service / always-on',
      'Federation, Hub fleet, SSO',
    ],
  },
} as const;
