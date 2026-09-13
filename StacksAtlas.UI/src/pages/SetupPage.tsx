import { Navigate } from 'react-router-dom';

/** Legacy route  -  redirects to license-first onboarding wizard. */
export default function SetupPage() {
  return <Navigate to="/onboarding" replace />;
}
