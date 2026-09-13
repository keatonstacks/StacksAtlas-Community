import React, { useState, useRef, useEffect, useCallback } from "react";
import { Box, TextField, Typography, CircularProgress } from "@mui/material";
import type { Device } from "../models/Device";
import { ApiService } from "../services/apiService";

interface EditableFieldProps {
    device: Device;
    fieldName: 'name' | 'location' | 'model' | 'vendor' | 'hostname';
    onUpdated?: (device?: Device) => void;
    disabled?: boolean;
    monospace?: boolean;
    placeholder?: string;
}

const SAVE_DEBOUNCE_MS = 500;

export function EditableField({ device, fieldName, onUpdated, disabled, monospace, placeholder }: EditableFieldProps) {
    const committedValue = device[fieldName] || "";
    const [draft, setDraft] = useState(committedValue);
    const [saving, setSaving] = useState(false);
    const [saved, setSaved] = useState(false);
    const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const draftRef = useRef(draft);
    const isDirtyRef = useRef(false);
    const savingRef = useRef(false);
    const committedRef = useRef(committedValue);
    const onUpdatedRef = useRef(onUpdated);
    onUpdatedRef.current = onUpdated;

    committedRef.current = committedValue;

    useEffect(() => {
        draftRef.current = draft;
    }, [draft]);

    // Sync from server only when the user is not mid-edit (avoids auto-refresh wiping typed text).
    useEffect(() => {
        if (!isDirtyRef.current && !savingRef.current) {
            setDraft(committedValue);
            draftRef.current = committedValue;
        }
    }, [device.id, committedValue]);

    const scheduleCommitRef = useRef<(next: string) => void>(() => {});

    const commit = useCallback(async (next: string) => {
        if (next === committedRef.current) {
            isDirtyRef.current = false;
            return;
        }

        savingRef.current = true;
        setSaving(true);
        setSaved(false);

        try {
            let updated: Device;
            if (fieldName === 'name') {
                updated = await ApiService.updateDeviceName(device.id, next) as Device;
            } else if (fieldName === 'location') {
                updated = await ApiService.updateDeviceLocation(device.id, next) as Device;
            } else if (fieldName === 'model') {
                updated = await ApiService.updateDeviceModel(device.id, next) as Device;
            } else if (fieldName === 'vendor') {
                updated = await ApiService.updateDeviceVendor(device.id, next) as Device;
            } else {
                updated = await ApiService.updateDeviceHostname(device.id, next) as Device;
            }

            // Keep dirty if the user typed more while this save was in flight.
            if (draftRef.current === next) {
                isDirtyRef.current = false;
                setSaved(true);
                setTimeout(() => setSaved(false), 1500);
            }
            onUpdatedRef.current?.(updated);
        } catch (err: unknown) {
            console.error("Save failed", err);
            // Only roll back if the user has not typed further.
            if (draftRef.current === next) {
                setDraft(committedRef.current);
                draftRef.current = committedRef.current;
                isDirtyRef.current = false;
            }
        } finally {
            savingRef.current = false;
            setSaving(false);
            if (isDirtyRef.current && draftRef.current !== committedRef.current) {
                scheduleCommitRef.current(draftRef.current);
            }
        }
    }, [device.id, fieldName]);

    const commitRef = useRef(commit);
    commitRef.current = commit;

    const scheduleCommit = useCallback((next: string) => {
        if (debounceRef.current) clearTimeout(debounceRef.current);
        debounceRef.current = setTimeout(() => {
            debounceRef.current = null;
            void commitRef.current(next);
        }, SAVE_DEBOUNCE_MS);
    }, []);
    scheduleCommitRef.current = scheduleCommit;

    const flushPending = useCallback(() => {
        if (debounceRef.current) {
            clearTimeout(debounceRef.current);
            debounceRef.current = null;
        }
        if (isDirtyRef.current) {
            void commitRef.current(draftRef.current);
        }
    }, []);

    useEffect(() => () => {
        if (debounceRef.current) {
            clearTimeout(debounceRef.current);
            debounceRef.current = null;
        }
        if (isDirtyRef.current) {
            void commitRef.current(draftRef.current);
        }
    }, []);

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const next = e.target.value;
        setDraft(next);
        draftRef.current = next;
        isDirtyRef.current = true;
        setSaved(false);
        scheduleCommit(next);
    };

    return (
        <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
            <TextField
                size="small"
                fullWidth
                value={draft}
                onChange={handleChange}
                onBlur={flushPending}
                onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        flushPending();
                    }
                }}
                disabled={disabled}
                placeholder={
                    placeholder
                    ?? (fieldName === 'name' ? device.ipAddress : fieldName === 'hostname' ? 'e.g. printer-east.local' : "Enter location...")
                }
                variant="standard"
                inputProps={{ 'aria-busy': saving || undefined }}
                sx={{
                    "& .MuiInput-root": {
                        fontSize: '0.875rem',
                        fontFamily: monospace ? 'monospace' : undefined,
                    },
                    "& .MuiInput-underline:before": { display: disabled ? 'none' : 'block' }
                }}
            />
            {saving && <CircularProgress size={14} aria-label="Saving" />}
            {saved && <Typography sx={{ color: "#4caf50", fontSize: "0.7rem" }}>Saved</Typography>}
        </Box>
    );
}
