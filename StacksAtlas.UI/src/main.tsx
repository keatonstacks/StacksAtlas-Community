import React, { useMemo, useState, Suspense } from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import { ThemeProvider, CssBaseline, createTheme, CircularProgress, Box } from "@mui/material";

import MainLayout from "./layout/MainLayout";
import { GlobalStatsProvider } from "./context/GlobalStatsContext";
import { ColumnVisibilityProvider } from "./context/ColumnVisibilityContext";
import { ConfirmProvider } from "./context/ConfirmContext";
import { AuthProvider, useAuth } from "./context/AuthContext";
import ProtectedRoute from "./components/ProtectedRoute";
import ApplianceOnlyRoute from "./components/ApplianceOnlyRoute";

import { lazyWithRetry } from "./utils/lazyWithRetry";

const DashboardPage = lazyWithRetry(() => import("./pages/DashboardPage"));
const DevicesPage = lazyWithRetry(() => import("./pages/DevicesPage"));
const AlertsPage = lazyWithRetry(() => import("./pages/AlertsPage"));
const SettingsPage = lazyWithRetry(() => import("./pages/SettingsPage"));
const ReportsPage = lazyWithRetry(() => import("./pages/ReportsPage"));
const LoginPage = lazyWithRetry(() => import("./pages/LoginPage"));
const SetupPage = lazyWithRetry(() => import("./pages/SetupPage"));
const OnboardingPage = lazyWithRetry(() => import("./pages/OnboardingPage"));
const EvaluationEndedPage = lazyWithRetry(() => import("./pages/EvaluationEndedPage"));
const UsersPage = lazyWithRetry(() => import("./pages/UsersPage"));
const LogsPage = lazyWithRetry(() => import("./pages/LogsPage"));
const AuditPage = lazyWithRetry(() => import("./pages/AuditPage"));
const ProfilePage = lazyWithRetry(() => import("./pages/ProfilePage"));

const RouteFallback = () => (
  <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '40vh' }}>
    <CircularProgress size={32} />
  </Box>
);



const RoleRoute = ({ element, allowedRoles }: { element: React.ReactElement, allowedRoles: string[] }) => {
  const { role, loading } = useAuth();
  if (loading) return null;
  if (!role || !allowedRoles.includes(role.toLowerCase())) {
    return <Navigate to="/" replace />;
  }
  return element;
};

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <AuthProvider>
      <GlobalStatsProvider>
        <ColumnVisibilityProvider>
          <ConfirmProvider>
            <App />
          </ConfirmProvider>
        </ColumnVisibilityProvider>
      </GlobalStatsProvider>
    </AuthProvider>
  </React.StrictMode>
);

function App() {
  const [mode, setMode] = useState<"light" | "dark">(
    (localStorage.getItem("themeMode") as "light" | "dark") || "dark"
  );

  const theme = useMemo(
    () =>
      createTheme({
        palette: {
          mode,
          primary: { main: "#1976d2" }
        },
        typography: {
          fontFamily: "'Inter', 'Segoe UI', 'Roboto', 'Helvetica', 'Arial', sans-serif",
          h1: { fontWeight: 700, letterSpacing: '-0.02em' },
          h2: { fontWeight: 700, letterSpacing: '-0.01em' },
          h3: { fontWeight: 700, letterSpacing: '-0.01em' },
          h4: { fontWeight: 700, letterSpacing: '-0.01em' },
          h5: { fontWeight: 600, letterSpacing: '0' },
          h6: { fontWeight: 600, letterSpacing: '0' },
          subtitle1: { fontWeight: 500 },
          subtitle2: { fontWeight: 600 },
          body1: { fontWeight: 400 },
          body2: { fontWeight: 400 },
          button: { fontWeight: 600, textTransform: 'none' },
          caption: { fontWeight: 500, letterSpacing: '0.02em' },
          overline: { fontWeight: 600, letterSpacing: '0.1em' }
        },
        components: {
          MuiButton: {
            styleOverrides: {
              root: { borderRadius: 8, padding: '8px 20px' }
            }
          },
          MuiTab: {
            styleOverrides: {
              root: { fontWeight: 600 }
            }
          }
        }
      }),
    [mode]
  );

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <BrowserRouter>
        <Suspense fallback={<RouteFallback />}>
        <Routes>
          {/* Public Routes */}
          <Route path="/login" element={<LoginPage />} />
          <Route path="/setup" element={<SetupPage />} />
          <Route path="/onboarding" element={<OnboardingPage />} />
          <Route path="/portable-closed" element={<EvaluationEndedPage />} />
          <Route path="/evaluation-ended" element={<EvaluationEndedPage />} />

          {/* Protected Routes */}
          <Route element={<ProtectedRoute />}>
            <Route
              path="/"
              element={<MainLayout mode={mode} setMode={setMode} />}
            >
              <Route index element={<DashboardPage />} />

              <Route path="devices" element={<DevicesPage />} />
              <Route path="alerts" element={<ApplianceOnlyRoute><AlertsPage /></ApplianceOnlyRoute>} />
              <Route path="reports" element={<ReportsPage />} />
              <Route
                path="settings"
                element={<RoleRoute allowedRoles={['admin', 'standard']} element={<SettingsPage mode={mode} setMode={setMode} />} />}
              />
              <Route path="users" element={<ApplianceOnlyRoute><RoleRoute allowedRoles={['admin']} element={<UsersPage />} /></ApplianceOnlyRoute>} />
              <Route path="logs" element={<RoleRoute allowedRoles={['admin']} element={<LogsPage />} />} />
              <Route path="audit" element={<RoleRoute allowedRoles={['admin']} element={<AuditPage />} />} />
              <Route path="profile" element={<ApplianceOnlyRoute><ProfilePage /></ApplianceOnlyRoute>} />

            </Route>
          </Route>
        </Routes>
        </Suspense>
      </BrowserRouter>
    </ThemeProvider>
  );
}
