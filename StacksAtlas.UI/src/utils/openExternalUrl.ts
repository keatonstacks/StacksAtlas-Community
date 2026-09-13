/**
 * Open ssh://, telnet://, etc. without navigating the SPA away from the current page.
 * Using window.location.href on a faux <a> can reload the app and collapse the device drawer.
 */
export function openExternalUrl(url: string): void {
  if (!url?.trim())
    return;

  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.rel = 'noopener noreferrer';
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
}
