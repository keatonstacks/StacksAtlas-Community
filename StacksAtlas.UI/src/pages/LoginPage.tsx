import React, { useState, useEffect } from 'react';
import { useAuth } from '../context/AuthContext';
import { ApiService } from '../services/apiService';
import { IconButton } from '@mui/material';
import Visibility from '@mui/icons-material/Visibility';
import VisibilityOff from '@mui/icons-material/VisibilityOff';
import { useNavigate } from 'react-router-dom';
import { APP_VERSION } from '../constants/version';
import API_BASE_URL from '../config/api';

const LoginPage: React.FC = () => {
    const [username, setUsername] = useState('');
    const [password, setPassword] = useState('');
    const [showPassword, setShowPassword] = useState(false);
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    const [ssoConfig, setSsoConfig] = useState<{ enabled: boolean, provider: string }>({ enabled: false, provider: '' });
    const auth = useAuth();
    const navigate = useNavigate();

    // Check if the appliance is initialized
    useEffect(() => {
        const checkStatus = async () => {
            try {
                const onboarding = await ApiService.getOnboardingStatus();
                if (onboarding?.isPortable) {
                    navigate('/onboarding', { replace: true });
                    return;
                }

                const status = await ApiService.checkAuthStatus();
                if (!status.isConfigured) {
                    navigate('/onboarding', { replace: true });
                } else {
                    const sso = await ApiService.getSsoConfig();
                    setSsoConfig(sso);
                }
            } catch (e) {
                console.error("Auth status/SSO check failed", e);
            }
        };
        checkStatus();
    }, [navigate]);

    // Aesthetic: Appliance Dark Mode (Consolidated with SetupPage style)
    const styles = {
        container: {
            display: 'flex',
            flexDirection: 'column' as const,
            alignItems: 'center',
            justifyContent: 'center',
            height: '100vh',
            backgroundColor: '#121212',
            color: '#e0e0e0',
            fontFamily: "'Inter', sans-serif"
        },
        card: {
            backgroundColor: '#1e1e1e',
            padding: '40px',
            borderRadius: '4px',
            boxShadow: '0 0 15px rgba(0,0,0,0.5)',
            width: '100%',
            maxWidth: '400px',
            border: '1px solid #333'
        },
        logo: {
            fontSize: '24px',
            fontWeight: '600' as const,
            marginBottom: '30px',
            textAlign: 'center' as const,
            color: '#1976d2',
            textTransform: 'uppercase' as const,
            letterSpacing: '1px'
        },
        inputGroup: {
            marginBottom: '20px',
            position: 'relative' as const
        },
        label: {
            display: 'block',
            marginBottom: '8px',
            fontSize: '12px',
            textTransform: 'uppercase' as const,
            color: '#666',
            fontWeight: 'bold' as const
        },
        input: {
            width: '100%',
            padding: '12px',
            backgroundColor: '#121212',
            border: '1px solid #333',
            borderRadius: '2px',
            color: '#fff',
            fontSize: '16px',
            boxSizing: 'border-box' as const,
            outline: 'none',
            fontFamily: 'monospace'
        },
        button: {
            width: '100%',
            padding: '14px',
            backgroundColor: '#1976d2',
            color: '#fff',
            border: 'none',
            borderRadius: '2px',
            fontSize: '14px',
            cursor: 'pointer',
            marginTop: '10px',
            fontWeight: 'bold' as const,
            textTransform: 'uppercase' as const,
            letterSpacing: '1px'
        },
        error: {
            backgroundColor: 'rgba(255, 82, 82, 0.1)',
            color: '#ff5252',
            padding: '10px',
            borderRadius: '2px',
            marginBottom: '20px',
            textAlign: 'center' as const,
            fontSize: '13px',
            border: '1px solid #ff5252'
        },
        eyeButton: {
            position: 'absolute' as const,
            right: '10px',
            top: '32px',
            color: '#666'
        },
        ssoButton: {
            width: '100%',
            padding: '12px',
            backgroundColor: 'transparent',
            color: '#1976d2',
            border: '1px solid #1976d2',
            borderRadius: '2px',
            fontSize: '13px',
            cursor: 'pointer',
            marginTop: '15px',
            fontWeight: 'bold' as const,
            textTransform: 'uppercase' as const,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            gap: '10px'
        },
        divider: {
            height: '1px',
            backgroundColor: '#333',
            margin: '20px 0',
            position: 'relative' as const,
            textAlign: 'center' as const
        },
        dividerText: {
            position: 'absolute' as const,
            top: '-10px',
            left: '50%',
            transform: 'translateX(-50%)',
            backgroundColor: '#1e1e1e',
            padding: '0 10px',
            color: '#666',
            fontSize: '10px',
            fontWeight: 'bold' as const
        },
    };

    const handleLogin = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');
        setLoading(true);

        try {
            const result = await ApiService.login({ username, password });
            auth.login(result.token, result.username);

            try {
                const ob = await ApiService.getOnboardingStatus();
                window.location.href = ob.isFoundationComplete ? '/' : '/onboarding';
            } catch {
                window.location.href = '/';
            }
        } catch (err: any) {
            setError(err.message || "Invalid credentials.");
            setLoading(false);
        }
    };

    return (
        <div style={styles.container}>
            <div style={styles.card}>
                <div style={styles.logo}>StacksAtlas Appliance</div>

                {error && <div style={styles.error}>{error}</div>}

                <form onSubmit={handleLogin}>
                    <div style={styles.inputGroup}>
                        <label style={styles.label}>Username</label>
                        <input
                            type="text"
                            style={styles.input}
                            value={username}
                            onChange={(e) => setUsername(e.target.value)}
                            autoFocus
                            spellCheck={false}
                        />
                    </div>
                    <div style={styles.inputGroup}>
                        <label style={styles.label}>Password</label>
                        <input
                            type={showPassword ? "text" : "password"}
                            style={styles.input}
                            value={password}
                            onChange={(e) => setPassword(e.target.value)}
                        />
                        <IconButton
                            style={styles.eyeButton}
                            onClick={() => setShowPassword(!showPassword)}
                            size="small"
                        >
                            {showPassword ? <VisibilityOff fontSize="small" /> : <Visibility fontSize="small" />}
                        </IconButton>
                    </div>
                    <button
                        type="submit"
                        style={styles.button}
                        disabled={loading}
                    >
                        {loading ? 'Authenticating...' : 'Connect'}
                    </button>
                </form>

                {ssoConfig.enabled && (
                    <>
                        <div style={styles.divider}>
                            <span style={styles.dividerText}>OR</span>
                        </div>
                        <button
                            style={styles.ssoButton}
                            onClick={() => window.location.href = `${API_BASE_URL}/api/auth/login/sso`}
                        >
                            Connect with {ssoConfig.provider}
                        </button>
                    </>
                )}
            </div>
            <div style={{ marginTop: '20px', color: '#444', fontSize: '12px', fontFamily: 'monospace' }}>
                ID: {window.location.hostname.toUpperCase()} | v{APP_VERSION}
            </div>
        </div>
    );
};

export default LoginPage;
