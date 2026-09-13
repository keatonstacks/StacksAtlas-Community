import { useEffect, useState } from 'react';
import { ApiService } from '../services/apiService';

/** Resolves whether the current session is StacksAtlas Portable (try mode). */
export function usePortableMode() {
  const [isPortable, setIsPortable] = useState<boolean | null>(null);

  useEffect(() => {
    ApiService.getSystemRuntimeInfo()
      .then((info) => setIsPortable(!!info?.isPortable))
      .catch(() => setIsPortable(false));
  }, []);

  return {
    isPortable: isPortable === true,
    isPortableReady: isPortable !== null,
  };
}
