function copyViaExecCommand(text: string): boolean {
  if (typeof document === 'undefined')
    return false;

  try {
    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.setAttribute('readonly', '');
    textarea.style.position = 'fixed';
    textarea.style.left = '-9999px';
    textarea.style.top = '0';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.focus();
    textarea.select();
    textarea.setSelectionRange(0, text.length);
    const ok = document.execCommand('copy');
    document.body.removeChild(textarea);
    return ok;
  } catch {
    return false;
  }
}

/** Copy text  -  works on LAN HTTP appliances where async Clipboard API is blocked. */
export async function copyTextToClipboard(text: string): Promise<boolean> {
  const value = text?.trim();
  if (!value)
    return false;

  // Synchronous fallback while the click user-gesture is still active (HTTP / non-secure contexts).
  if (copyViaExecCommand(value))
    return true;

  if (typeof navigator !== 'undefined' && window.isSecureContext && navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(value);
      return true;
    } catch {
      return copyViaExecCommand(value);
    }
  }

  return false;
}
