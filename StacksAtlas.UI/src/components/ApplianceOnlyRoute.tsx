import React, { useEffect, useState } from 'react';
import { Navigate } from 'react-router-dom';
import { ApiService } from '../services/apiService';

interface ApplianceOnlyRouteProps {
  children: React.ReactElement;
}

/** Redirects portable sessions away from appliance-only pages (Users, Alerts, etc.). */
const ApplianceOnlyRoute: React.FC<ApplianceOnlyRouteProps> = ({ children }) => {
  const [isPortable, setIsPortable] = useState<boolean | null>(null);

  useEffect(() => {
    ApiService.getSystemRuntimeInfo()
      .then((info) => setIsPortable(!!info?.isPortable))
      .catch(() => setIsPortable(false));
  }, []);

  if (isPortable === null) {
    return null;
  }

  if (isPortable) {
    return <Navigate to="/" replace />;
  }

  return children;
};

export default ApplianceOnlyRoute;
