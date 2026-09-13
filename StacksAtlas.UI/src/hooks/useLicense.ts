import { useState, useEffect, useCallback } from 'react';
import { ApiService } from '../services/apiService';

export interface LicenseStatus {
    isActive: boolean;
    tier: number | string;
    deviceLimit: number;
    licenseKey: string | null;
    hardwareId: string;
    message: string | null;
    currentCount: number;
    isLimited: boolean;
    nodeLimit: number;
    hubLimit: number;
    ssoEnabled: boolean;
    webhooksEnabled: boolean;
    apiEnabled: boolean;
    allowsHub?: boolean;
    allowsFederationJoin?: boolean;
    maxStandaloneActivations?: number;
    billingModel?: number;
}

export const useLicense = () => {
    const [status, setStatus] = useState<LicenseStatus | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    const fetchStatus = useCallback(async () => {
        try {
            setLoading(true);
            const data = await ApiService.getLicenseStatus();
            setStatus(data);
            setError(null);
        } catch (err: any) {
            setError(err.message || "Failed to fetch license status");
        } finally {
            setLoading(false);
        }
    }, []);

    const activate = async (key: string) => {
        try {
            setLoading(true);
            const data = await ApiService.activateLicense(key);
            setStatus(data);
            if (data.isActive) {
                setError(null);
                return { success: true, message: data.message };
            } else {
                setError(data.message || "Activation failed");
                return { success: false, message: data.message };
            }
        } catch (err: any) {
            setError(err.message || "Activation failed");
            return { success: false, message: err.message };
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchStatus();
    }, [fetchStatus]);

    return { status, loading, error, refresh: fetchStatus, activate };
};
