import { describe, expect, it } from 'vitest';
import { resolveVendorIconSrc, vendorMatchesKey } from './vendorIconMap';
import dellIcon from '../assets/vendors/Dell.svg';
import logitechIcon from '../assets/vendors/logitech.svg';
import lgIcon from '../assets/vendors/lg.svg';

describe('vendorMatchesKey', () => {
  it('rejects logi inside technologies', () => {
    expect(vendorMatchesKey('dell technologies', 'logi')).toBe(false);
  });

  it('accepts dell at start of dell inc.', () => {
    expect(vendorMatchesKey('dell inc.', 'dell')).toBe(true);
  });

  it('accepts logitech as a whole token', () => {
    expect(vendorMatchesKey('logitech international', 'logitech')).toBe(true);
  });
});

describe('resolveVendorIconSrc', () => {
  it('maps Dell Technologies to Dell logo (not Logitech)', () => {
    expect(resolveVendorIconSrc('Dell Technologies')).toBe(dellIcon);
  });

  it('maps Dell Inc. to Dell logo', () => {
    expect(resolveVendorIconSrc('Dell Inc.')).toBe(dellIcon);
  });

  it('maps Logitech vendors to Logitech logo', () => {
    expect(resolveVendorIconSrc('Logitech')).toBe(logitechIcon);
    expect(resolveVendorIconSrc('Logitech International')).toBe(logitechIcon);
  });

  it('maps LG Electronics without matching unrelated vendors', () => {
    expect(resolveVendorIconSrc('LG Electronics')).toBe(lgIcon);
    expect(resolveVendorIconSrc('Dell')).toBe(dellIcon);
  });

  it('returns null for unknown vendors', () => {
    expect(resolveVendorIconSrc('Acme Corp')).toBeNull();
    expect(resolveVendorIconSrc(null)).toBeNull();
  });
});
