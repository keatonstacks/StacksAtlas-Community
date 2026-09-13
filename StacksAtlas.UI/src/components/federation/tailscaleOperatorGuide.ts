/** External links and copy-paste snippets for Tailscale remote federation (Phase 7.4 groundwork). */

export const TAILSCALE_DOWNLOAD_URL = 'https://tailscale.com/download';
export const TAILSCALE_ADMIN_KEYS_URL = 'https://login.tailscale.com/admin/settings/keys';
export const TAILSCALE_ACL_DOCS_URL = 'https://tailscale.com/kb/1018/acls';

/** Recommended ACL for tagged Hub + Node fleet (ports 5001 UI/API, 5002 mTLS sync). */
export const TAILSCALE_ACL_TAGGED_FLEET = `{
  "tagOwners": {
    "tag:stacksatlas-hub": ["autogroup:admin"],
    "tag:stacksatlas-node": ["autogroup:admin"]
  },
  "acls": [
    {
      "action": "accept",
      "src": ["tag:stacksatlas-node"],
      "dst": ["tag:stacksatlas-hub:5001", "tag:stacksatlas-hub:5002"]
    },
    {
      "action": "accept",
      "src": ["tag:stacksatlas-hub"],
      "dst": ["tag:stacksatlas-node:5001", "tag:stacksatlas-node:5002"]
    }
  ]
}`;

/** Homelab / small tailnet  -  any member can reach federation ports on other members. */
export const TAILSCALE_ACL_HOMELAB = `{
  "acls": [
    {
      "action": "accept",
      "src": ["autogroup:member"],
      "dst": ["autogroup:member:5001", "autogroup:member:5002"]
    }
  ]
}`;

export type RemoteSiteChecklistStep = {
  title: string;
  body: string;
  href?: string;
  hrefLabel?: string;
};

export const REMOTE_SITE_CHECKLIST_STEPS: RemoteSiteChecklistStep[] = [
  {
    title: 'Install Tailscale on this Node',
    body: 'Install the Tailscale client on the host running StacksAtlas (not inside an isolated container unless you use host networking or mount tailscaled.sock).',
    href: TAILSCALE_DOWNLOAD_URL,
    hrefLabel: 'Download Tailscale',
  },
  {
    title: 'Join your organization tailnet',
    body:
      'A tailnet is your private Tailscale network. Create a pre-auth key in the Tailscale admin console (reusable for multiple appliances, or one-off for a single site). ' +
      'On this host, run tailscale up and paste that key when prompted  -  or sign in via the Tailscale app on Windows/macOS. ' +
      'Hub and every edge Node must appear in the same tailnet (same Tailscale account/organization) before federation can work.',
    href: TAILSCALE_ADMIN_KEYS_URL,
    hrefLabel: 'Create a pre-auth key',
  },
  {
    title: 'Configure Hub tailnet identity below',
    body: 'Enable "Use Tailscale for Hub Connection", enter the Hub MagicDNS hostname and 100.x tailnet IP (from Hub Settings → Federation), then Save & Restart.',
  },
  {
    title: 'Verify reachability',
    body: 'Use "Test Hub Reachability via Tailscale" below. A successful probe on port 5002 confirms mTLS enrollment can complete.',
  },
  {
    title: 'Enroll with a Tailscale (Remote) string',
    body: 'On the Hub, generate a Tailscale (Remote) enrollment string (Hub Dashboard or Settings → Federation). Paste it in Sovereign mTLS enrollment in the Federation section above.',
  },
];
