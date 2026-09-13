import appleIcon from '../assets/vendors/apple.svg';
import ciscoIcon from '../assets/vendors/cisco.svg';
import crestronIcon from '../assets/vendors/crestron.svg';
import microsoftIcon from '../assets/vendors/microsoft.svg';
import ubiquitiIcon from '../assets/vendors/ubiquiti.svg';
import lgIcon from '../assets/vendors/lg.svg';
import biampIcon from '../assets/vendors/biamp.svg';
import logitechIcon from '../assets/vendors/logitech.svg';
import polyIcon from '../assets/vendors/poly.svg';
import polycomIcon from '../assets/vendors/polycom.svg';
import jblIcon from '../assets/vendors/jbl.svg';
import boseIcon from '../assets/vendors/bose.svg';
import sonyIcon from '../assets/vendors/sony.svg';
import qscIcon from '../assets/vendors/qsc.svg';
import extronIcon from '../assets/vendors/extron.svg';
import belkinIcon from '../assets/vendors/belkin.svg';
import shureIcon from '../assets/vendors/shure.svg';
import onkyoIcon from '../assets/vendors/onkyo.svg';
import averIcon from '../assets/vendors/aver.svg';
import wyrestormIcon from '../assets/vendors/wyrestorm.svg';
import clearoneIcon from '../assets/vendors/clearone.svg';
import stacksAtlasIcon from '../assets/vendors/StacksAtlas.svg';
import dellIcon from '../assets/vendors/Dell.svg';
import synologyIcon from '../assets/vendors/Synology.svg';
import akgIcon from '../assets/vendors/akg.svg';
import arubaIcon from '../assets/vendors/aruba-hp.svg';
import audioTechnicaIcon from '../assets/vendors/audio-technica.svg';
import bangOlufsenIcon from '../assets/vendors/bang-olufsen.svg';
import barcoIcon from '../assets/vendors/barco.svg';
import benqIcon from '../assets/vendors/benq.svg';
import beyerdynamicIcon from '../assets/vendors/beyerdynamic.svg';
import canonIcon from '../assets/vendors/canon.svg';
import denonIcon from '../assets/vendors/denon.svg';
import dynaudioIcon from '../assets/vendors/dynaudio.svg';
import focalIcon from '../assets/vendors/focal.svg';
import genelecIcon from '../assets/vendors/genelec.svg';
import infocusIcon from '../assets/vendors/infocus.svg';
import jvcIcon from '../assets/vendors/jvc-professional.svg';
import klipschIcon from '../assets/vendors/klipsch.svg';
import kramerIcon from '../assets/vendors/kramer.svg';
import mackieIcon from '../assets/vendors/mackie.svg';
import marantzIcon from '../assets/vendors/marantz.svg';
import necIcon from '../assets/vendors/nec.svg';
import netgearIcon from '../assets/vendors/netgear.svg';
import pioneerIcon from '../assets/vendors/pioneer.svg';
import samsungIcon from '../assets/vendors/samsung-electronics.svg';
import sennheiserIcon from '../assets/vendors/sennheiser.svg';
import sharpIcon from '../assets/vendors/sharp.svg';
import tannoyIcon from '../assets/vendors/tannoy.svg';
import viewsonicIcon from '../assets/vendors/viewsonic.svg';
import yamahaIcon from '../assets/vendors/yamaha.svg';

/** Lowercase vendor label → logo asset path */
export const VENDOR_ICON_MAP: Record<string, string> = {
  apple: appleIcon,
  'apple inc.': appleIcon,
  cisco: ciscoIcon,
  'cisco systems': ciscoIcon,
  'cisco systems, inc.': ciscoIcon,
  crestron: crestronIcon,
  'crestron electronics': crestronIcon,
  'crestron electronics, inc.': crestronIcon,
  microsoft: microsoftIcon,
  'microsoft corporation': microsoftIcon,
  ubiquiti: ubiquitiIcon,
  'ubiquiti networks': ubiquitiIcon,
  'ubiquiti inc.': ubiquitiIcon,
  unifi: ubiquitiIcon,
  lg: lgIcon,
  'lg electronics': lgIcon,
  biamp: biampIcon,
  'biamp systems': biampIcon,
  logitech: logitechIcon,
  logi: logitechIcon,
  poly: polyIcon,
  polycom: polycomIcon,
  'hp poly': polyIcon,
  jbl: jblIcon,
  'jbl professional': jblIcon,
  bose: boseIcon,
  'bose professional': boseIcon,
  sony: sonyIcon,
  'sony electronics': sonyIcon,
  qsc: qscIcon,
  'q-sys': qscIcon,
  'qsc audio': qscIcon,
  extron: extronIcon,
  'extron electronics': extronIcon,
  belkin: belkinIcon,
  'belkin international': belkinIcon,
  shure: shureIcon,
  'shure incorporated': shureIcon,
  onkyo: onkyoIcon,
  aver: averIcon,
  'aver information': averIcon,
  wyrestorm: wyrestormIcon,
  'wyrestorm technologies': wyrestormIcon,
  clearone: clearoneIcon,
  stacksatlas: stacksAtlasIcon,
  akg: akgIcon,
  'akg acoustics': akgIcon,
  aruba: arubaIcon,
  'hewlett packard enterprise': arubaIcon,
  hpe: arubaIcon,
  'audio-technica': audioTechnicaIcon,
  'audio technica': audioTechnicaIcon,
  'bang & olufsen': bangOlufsenIcon,
  'b&o': bangOlufsenIcon,
  barco: barcoIcon,
  benq: benqIcon,
  beyerdynamic: beyerdynamicIcon,
  canon: canonIcon,
  denon: denonIcon,
  dynaudio: dynaudioIcon,
  focal: focalIcon,
  genelec: genelecIcon,
  infocus: infocusIcon,
  jvc: jvcIcon,
  'jvc professional': jvcIcon,
  klipsch: klipschIcon,
  kramer: kramerIcon,
  'kramer electronics': kramerIcon,
  mackie: mackieIcon,
  marantz: marantzIcon,
  nec: necIcon,
  'nec corporation': necIcon,
  netgear: netgearIcon,
  pioneer: pioneerIcon,
  samsung: samsungIcon,
  'samsung electronics': samsungIcon,
  sennheiser: sennheiserIcon,
  sharp: sharpIcon,
  tannoy: tannoyIcon,
  viewsonic: viewsonicIcon,
  yamaha: yamahaIcon,
  dell: dellIcon,
  'dell inc.': dellIcon,
  'dell technologies': dellIcon,
  'dell emc': dellIcon,
  synology: synologyIcon,
  'synology inc.': synologyIcon,
};

const VENDOR_KEYS_BY_LENGTH = Object.keys(VENDOR_ICON_MAP).sort((a, b) => b.length - a.length);

/** True when `needle` appears as a whole token in `vendor` (avoids logi ⊂ technologies). */
export function vendorMatchesKey(vendor: string, key: string): boolean {
  if (vendor === key) return true;
  const idx = vendor.indexOf(key);
  if (idx === -1) return false;
  const beforeOk = idx === 0 || !/[a-z0-9]/.test(vendor[idx - 1]!);
  const afterOk =
    idx + key.length === vendor.length || !/[a-z0-9]/.test(vendor[idx + key.length]!);
  return beforeOk && afterOk;
}

/** Resolve vendor logo path; null when no known brand match. */
export function resolveVendorIconSrc(vendor: string | null | undefined): string | null {
  if (!vendor) return null;
  const v = vendor.toLowerCase().trim();
  if (VENDOR_ICON_MAP[v]) return VENDOR_ICON_MAP[v]!;

  for (const key of VENDOR_KEYS_BY_LENGTH) {
    if (vendorMatchesKey(v, key)) return VENDOR_ICON_MAP[key]!;
  }
  return null;
}
