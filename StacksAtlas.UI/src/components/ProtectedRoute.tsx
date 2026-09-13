import React, { useEffect, useState } from 'react';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { ApiService } from '../services/apiService';
import type { OnboardingStatus } from '../models/Onboarding';

const loadingBox = (
  <div style={{
    height: '100vh',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: '#0A1929',
    color: '#64748B',
    fontFamily: "'Inter', sans-serif",
  }}>
    Loading appliance...
  </div>
);

const ProtectedRoute: React.FC = () => {
  const { isAuthenticated, loading } = useAuth();
  const location = useLocation();
  const [serverInitialized, setServerInitialized] = useState<boolean | null>(null);
  const [onboarding, setOnboarding] = useState<OnboardingStatus | null>(null);
  const [onboardingChecked, setOnboardingChecked] = useState(false);

  useEffect(() => {
    let cancelled = false;

    const checkStatus = async () => {
      while (!cancelled) {
        try {
          const authStatus = await ApiService.checkAuthStatus();
          const ob = await ApiService.getOnboardingStatus();
          if (cancelled) return;
          setServerInitialized(authStatus.isConfigured);
          setOnboarding(ob);
          setOnboardingChecked(true);
          return;
        } catch (e) {
          console.error('Bootstrap gate check failed', e);
          await new Promise((resolve) => setTimeout(resolve, 1000));
        }
      }
    };

    checkStatus();
    return () => {
      cancelled = true;
    };
  }, [location.pathname]);

  if (loading || serverInitialized === null || !onboardingChecked) {
    return loadingBox;
  }

  if (!serverInitialized) {
    return <Navigate to="/onboarding" replace />;
  }

  if (!isAuthenticated) {
    if (onboarding?.isPortable) {
      return <Navigate to="/onboarding" replace />;
    }
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  if (onboarding && !onboarding.isFoundationComplete) {
    return <Navigate to="/onboarding" replace />;
  }

  return <Outlet />;
};

export default ProtectedRoute;
