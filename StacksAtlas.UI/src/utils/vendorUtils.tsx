import { Box, Tooltip } from '@mui/material';
import { resolveVendorIconSrc } from './vendorIconMap';

export { resolveVendorIconSrc, vendorMatchesKey, VENDOR_ICON_MAP } from './vendorIconMap';

export const getVendorIcon = (vendor: string | null | undefined) => {
  const iconSrc = resolveVendorIconSrc(vendor);
  if (!iconSrc) return null;

  return (
    <Tooltip title={vendor} arrow>
      <Box
        component="img"
        src={iconSrc}
        alt={vendor ?? ''}
        sx={{
          width: 24,
          height: 20,
          maxWidth: 32,
          objectFit: 'contain',
          display: 'block',
          filter: 'drop-shadow(0 0 2px rgba(0,0,0,0.1))',
        }}
      />
    </Tooltip>
  );
};
