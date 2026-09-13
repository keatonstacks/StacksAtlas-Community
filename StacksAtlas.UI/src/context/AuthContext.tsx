import React, { createContext, useContext, useState, useEffect } from 'react';
import { ApiService } from '../services/apiService';

interface AuthContextType {
    isAuthenticated: boolean;
    user: { username: string; role: string } | null;
    role: string | null;
    userId: string | null;
    token: string | null;
    provider: string | null;
    login: (token: string, username: string) => void;
    logout: () => void;
    loading: boolean;
    isAdmin: boolean;
    isExternal: boolean;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

function parseJwt(token: string) {
    try {
        const base64Url = token.split('.')[1];
        const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
        const jsonPayload = decodeURIComponent(window.atob(base64).split('').map(function (c) {
            return '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2);
        }).join(''));

        return JSON.parse(jsonPayload);
    } catch (e) {
        return null;
    }
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
    const [token, setToken] = useState<string | null>(localStorage.getItem('token'));
    const [user, setUser] = useState<string | null>(localStorage.getItem('username'));
    const [role, setRole] = useState<string | null>(localStorage.getItem('userRole'));
    const [userId, setUserId] = useState<string | null>(localStorage.getItem('userId'));
    const [provider, setProvider] = useState<string | null>(localStorage.getItem('userProvider'));
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        const completeSsoHandoff = async () => {
            try {
                const response = await fetch('/api/auth/sso/complete', { credentials: 'include' });
                if (response.ok && response.status !== 204) {
                    const data = await response.json();
                    if (data?.token) {
                        login(data.token, data.username || 'sso_user');
                        const url = new URL(window.location.href);
                        url.searchParams.delete('sso');
                        window.history.replaceState({}, document.title, url.pathname + url.search);
                    }
                }
            } catch {
                // No SSO handoff cookie  -  normal load.
            }
        };

        // Legacy: strip ?token= from URL if present (pre-1.7.9 bookmarks); do not store.
        const urlParams = new URLSearchParams(window.location.search);
        if (urlParams.has('token')) {
            urlParams.delete('token');
            const clean = urlParams.toString();
            window.history.replaceState({}, document.title, window.location.pathname + (clean ? `?${clean}` : ''));
        }

        void completeSsoHandoff();

        const storedToken = localStorage.getItem('token');
        if (storedToken) {
            const decoded = parseJwt(storedToken);
            if (decoded) {
                const r = decoded["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] || decoded.role;
                setRole(r);
                setUserId(decoded.id);
                setProvider(decoded.provider || 'Local');
            }
            ApiService.setToken(storedToken);
        }
        setLoading(false);
    }, []);

    // Silent Refresh Logic
    useEffect(() => {
        if (!token) return;

        const refreshInterval = setInterval(async () => {
            const decoded = parseJwt(token);
            if (!decoded) return;

            const now = Math.floor(Date.now() / 1000);
            const exp = decoded.exp;
            
            // Refresh if token expires in less than 30 minutes
            if (exp - now < 1800) {
                try {
                    const response = await fetch('/api/auth/refresh', {
                        method: 'POST',
                        headers: {
                            'Authorization': `Bearer ${token}`
                        }
                    });
                    
                    if (response.ok) {
                        const data = await response.json();
                        login(data.token, data.username);
                    } else if (response.status === 401) {
                        logout();
                    }
                } catch (error) {
                    console.error("Failed to refresh token", error);
                }
            }
        }, 60000); // Check every minute

        return () => clearInterval(refreshInterval);
    }, [token]);

    const login = (newToken: string, newUser: string) => {
        const decoded = parseJwt(newToken);
        const newRole = decoded ? (decoded["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] || decoded.role) : "Standard";
        const newUserId = decoded ? decoded.id : null;
        const newProvider = decoded ? (decoded.provider || 'Local') : 'Local';

        localStorage.setItem('token', newToken);
        localStorage.setItem('username', newUser);
        localStorage.setItem('userRole', newRole);
        localStorage.setItem('userProvider', newProvider);
        if (newUserId) localStorage.setItem('userId', newUserId);

        setToken(newToken);
        setUser(newUser);
        setRole(newRole);
        setUserId(newUserId);
        setProvider(newProvider);
        ApiService.setToken(newToken);
    };

    const logout = () => {
        localStorage.clear();
        setToken(null);
        setUser(null);
        setRole(null);
        setUserId(null);
        ApiService.setToken(null);
        void (async () => {
            let target = '/login';
            try {
                const onboarding = await ApiService.getOnboardingStatus();
                if (onboarding?.isPortable) {
                    target = '/onboarding';
                }
            } catch {
                // Fall back to login when status is unavailable.
            }
            window.location.href = target;
        })();
    };

    const value = {
        isAuthenticated: !!token,
        user: user ? { username: user, role: role || '' } : null,
        role,
        userId,
        token,
        provider,
        login,
        logout,
        loading,
        isAdmin: role?.toLowerCase() === 'admin',
        isExternal: (provider?.toLowerCase() !== 'local' && !!provider)
    };

    return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
    const context = useContext(AuthContext);
    if (context === undefined) {
        throw new Error('useAuth must be used within an AuthProvider');
    }
    return context;
}
