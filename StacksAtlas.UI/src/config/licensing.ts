/** 2-tier licensing copy at 1.8  -  Free portable + Business. */
export const TIER_PLAN_COPY: Record<string, { title: string; body: string; price?: string }> = {
  Free: {
    title: 'Free (Portable)',
    body: 'Session scanner on Win/Mac/Docker  -  no license key. Local discovery and monitoring.',
    price: '$0',
  },
  Business: {
    title: 'Business',
    body: 'Always-on appliance  -  one $40 license per machine. Hub mode, federation, SSO, unlimited devices per site.',
    price: '$40',
  },
  Home: {
    title: 'Free (Portable)',
    body: 'Session scanner  -  no license key required for portable mode.',
    price: '$0',
  },
  Pro: {
    title: 'Business',
    body: 'Legacy Pro maps to Business tier in 1.8 UI.',
    price: '$40',
  },
  Enterprise: {
    title: 'Enterprise',
    body: 'Custom scale and consulting  -  contact sales.',
  },
};

/** Values at or above this are treated as "unlimited" standalone activations in UI copy. */
export const UNLIMITED_STANDALONE_ACTIVATIONS_THRESHOLD = 1_000_000;
