import { useEffect, useState, useMemo, useRef } from "react";
import { isHubMode, normalizeExecutionMode } from '../utils/federationMode';
import {
    Box, Typography, Paper, Table, TableBody, TableCell, TableContainer,
    TableHead, TableRow, IconButton, Button, Dialog, DialogTitle,
    DialogContent, DialogActions, TextField, MenuItem, Select,
    FormControl, InputLabel, Chip, useTheme, alpha, CircularProgress,
    InputAdornment, TableSortLabel, Stack, Tooltip,
    Snackbar, Alert
} from "@mui/material";
import {
    Delete as DeleteIcon,
    PersonAdd as AddIcon,
    Shield as ShieldIcon,
    Badge as BadgeIcon,
    Visibility as ViewerIcon,
    Search as SearchIcon,
    FileDownload as DownloadIcon,
    FileUpload as UploadIcon,
    VpnKey as PasswordIcon,
    NotificationsActive as AlertIcon,
    Public as GlobalIcon
} from "@mui/icons-material";


import { ApiService } from "../services/apiService";
import { PageHeader } from "../components/pageheader";
import { useAuth } from "../context/AuthContext";
import { useConfirm } from "../context/ConfirmContext";
import { useIsMobileLayout } from "../hooks/useIsMobileLayout";
import { MobileUserList } from "../components/users/mobile/MobileUserList";
import type { User } from "../models/User";
import UserDrawer from "../components/UserDrawer";
import { PageShell } from "../components/mobile/PageShell";

type Order = 'asc' | 'desc';

export default function UsersPage() {
    const theme = useTheme();
    const isMobile = useIsMobileLayout();
    const { user } = useAuth();
    const { confirm } = useConfirm();
    const [users, setUsers] = useState<User[]>([]);
    const [loading, setLoading] = useState(true);
    const [searchTerm, setSearchTerm] = useState("");
    const [orderBy, setOrderBy] = useState<keyof User>('username');
    const [order, setOrder] = useState<Order>('asc');

    // States for various dialogs/menus
    const [openAdd, setOpenAdd] = useState(false);
    const [openReset, setOpenReset] = useState(false);
    const [selectedUser, setSelectedUser] = useState<User | null>(null);
    const [drawerOpen, setDrawerOpen] = useState(false);

    // Form states
    const [newUsername, setNewUsername] = useState("");
    const [newPassword, setNewPassword] = useState("");
    const [newEmail, setNewEmail] = useState("");
    const [newRole, setNewRole] = useState("Standard");
    const [resetPassword, setResetPassword] = useState("");

    const [isNode, setIsNode] = useState(false);
    const [isHub, setIsHub] = useState(false);
    const [currentNodeId, setCurrentNodeId] = useState<string | null>(null);

    // Feedback states
    const [snackbar, setSnackbar] = useState<{ open: boolean, message: string, severity: 'success' | 'error' }>({
        open: false,
        message: "",
        severity: 'success'
    });

    const fileInputRef = useRef<HTMLInputElement>(null);

    useEffect(() => {
        loadUsers();
        ApiService.getFederationSettings().then(settings => {
            if (settings) {
                // Only lock user management if in Node mode AND user synchronization is actively enabled by the Hub
                setIsNode(normalizeExecutionMode(settings.mode) === 0 && !!settings.hubUrl && (settings.syncUserRegistry || settings.syncUsers));
                setIsHub(isHubMode(settings.mode));
                setCurrentNodeId(settings.nodeId || null);
            }
        }).catch(err => console.error("Failed to load federation settings", err));
    }, []);

    const loadUsers = async () => {
        try {
            setLoading(true);
            const data = await ApiService.getUsers();
            setUsers(data);
        } catch (error) {
            console.error("Failed to load users", error);
        } finally {
            setLoading(false);
        }
    };

    const handleSort = (property: keyof User) => {
        const isAsc = orderBy === property && order === 'asc';
        setOrder(isAsc ? 'desc' : 'asc');
        setOrderBy(property);
    };

    const sortedAndFilteredUsers = useMemo(() => {
        return users
            .filter(u =>
                u.username.toLowerCase().includes(searchTerm.toLowerCase()) ||
                u.email?.toLowerCase().includes(searchTerm.toLowerCase()) ||
                u.role.toLowerCase().includes(searchTerm.toLowerCase())
            )
            .sort((a, b) => {
                const aValue = (a[orderBy] || '').toString().toLowerCase();
                const bValue = (b[orderBy] || '').toString().toLowerCase();

                if (order === 'asc') {
                    return aValue < bValue ? -1 : 1;
                } else {
                    return aValue > bValue ? -1 : 1;
                }
            });
    }, [users, searchTerm, orderBy, order]);

    const handleCreateUser = async () => {
        const isValid = newUsername && (newRole === "AlertOnly" ? newEmail : newPassword);
        if (!isValid) return;
        try {
            await ApiService.createUser(newUsername, newPassword, newRole, newEmail || undefined);
            setOpenAdd(false);
            setNewUsername("");
            setNewPassword("");
            setNewEmail("");
            setNewRole("Standard");
            loadUsers();
            setSnackbar({ open: true, message: "User created successfully", severity: 'success' });
        } catch {
            setSnackbar({ open: true, message: "Failed to create user", severity: 'error' });
        }
    };

    const handleDeleteUser = async (id: string) => {
        const ok = await confirm({
            title: 'Delete user',
            message: 'Are you sure you want to delete this user?',
            confirmLabel: 'Delete',
            confirmColor: 'error',
        });
        if (!ok) return;
        try {
            await ApiService.deleteUser(id);
            loadUsers();
            setSnackbar({ open: true, message: "User deleted", severity: 'success' });
        } catch {
            setSnackbar({ open: true, message: "Failed to delete user", severity: 'error' });
        }
    };

    const handleResetPassword = async () => {
        if (!selectedUser || !resetPassword) return;
        try {
            await ApiService.resetUserPassword(selectedUser.id, resetPassword);
            setOpenReset(false);
            setResetPassword("");
            setSnackbar({ open: true, message: "Password reset successfully", severity: 'success' });
        } catch {
            setSnackbar({ open: true, message: "Failed to reset password", severity: 'error' });
        }
    };

    const handleExportCSV = () => {
        const headers = ["Username", "Email", "Role", "Created At", "Last Login"];
        const rows = sortedAndFilteredUsers.map(u => [
            u.username,
            u.email || "",
            u.role,
            new Date(u.createdAt).toLocaleString(),
            u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : "Never"
        ]);

        const csvContent = [headers, ...rows].map(e => e.join(",")).join("\n");
        const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
        const link = document.createElement("a");
        const url = URL.createObjectURL(blob);
        link.setAttribute("href", url);
        link.setAttribute("download", `StacksAtlas_users_${new Date().toISOString().split('T')[0]}.csv`);
        link.style.visibility = 'hidden';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    };

    const downloadSampleCSV = () => {
        const headers = ["Username", "Password", "Role", "Email"];
        const sample = ["jdoe", "P@ssword123", "Standard", "jdoe@example.com"];
        const csvContent = [headers, sample].map(e => e.join(",")).join("\n");
        const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
        const link = document.createElement("a");
        const url = URL.createObjectURL(blob);
        link.setAttribute("href", url);
        link.setAttribute("download", "StacksAtlas_user_import_sample.csv");
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    };

    const handleImportCSV = (event: React.ChangeEvent<HTMLInputElement>) => {
        const file = event.target.files?.[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = async (e) => {
            const text = e.target?.result as string;
            const lines = text.split("\n").filter(l => l.trim().length > 0);

            let successCount = 0;
            let failCount = 0;

            for (let i = 1; i < lines.length; i++) {
                const values = lines[i].split(",");
                const username = values[0]?.trim();
                const password = values[1]?.trim();
                const role = values[2]?.trim() || "Standard";
                const email = values[3]?.trim() || undefined;

                if (username && password) {
                    try {
                        await ApiService.createUser(username, password, role, email);
                        successCount++;
                    } catch {
                        failCount++;
                    }
                }
            }

            setSnackbar({
                open: true,
                message: `Import complete. ${successCount} added, ${failCount} failed.`,
                severity: successCount > 0 ? 'success' : 'error'
            });
            loadUsers();
            if (fileInputRef.current) fileInputRef.current.value = "";
        };
        reader.readAsText(file);
    };

    const getRoleChipColor = (role: string) => {
        switch (role) {
            case "Admin": return "error";
            case "Standard": return "primary";
            case "Viewer": return "default";
            case "AlertOnly": return "secondary";
            default: return "default";
        }
    };

    const getRoleIcon = (role: string) => {
        switch (role) {
            case "Admin": return <ShieldIcon sx={{ fontSize: 16 }} />;
            case "Standard": return <BadgeIcon sx={{ fontSize: 16 }} />;
            case "Viewer": return <ViewerIcon sx={{ fontSize: 16 }} />;
            case "AlertOnly": return <AlertIcon sx={{ fontSize: 16 }} />;
            default: return null;
        }
    };

    return (
        <PageShell isMobile={isMobile}>
            <PageHeader
                title="User Management"
                subtitle="Manage system access, roles, and security credentials."
                stats={[
                    { label: "Total Users", value: users.length },
                    { label: "Active Now", value: users.filter(u => u.lastLoginAt && new Date(u.lastLoginAt) > new Date(Date.now() - 3600000)).length, color: "success.main" }
                ]}
            />

            {isNode && (
                <Box 
                    sx={{ 
                        p: 2.5, 
                        mb: 3, 
                        borderRadius: 3, 
                        bgcolor: alpha(theme.palette.primary.main, 0.05),
                        border: `1px solid ${alpha(theme.palette.primary.main, 0.25)}`,
                        backdropFilter: "blur(10px)",
                        display: 'flex', 
                        flexDirection: isMobile ? 'column' : 'row',
                        alignItems: isMobile ? 'flex-start' : 'center', 
                        gap: 2,
                        boxShadow: `0 4px 20px ${alpha(theme.palette.primary.main, 0.05)}`
                    }}
                >
                    <ShieldIcon color="primary" sx={{ fontSize: 28 }} />
                    <Box>
                        <Typography variant="subtitle2" sx={{ fontWeight: 700, letterSpacing: -0.2 }}>
                            GLOBAL IDENTITY GOVERNANCE ACTIVE
                        </Typography>
                        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.25, fontWeight: 500 }}>
                            This appliance is federated to your StacksAtlas Hub. User accounts, roles, and SSO credentials are governed globally. Local controls are locked.
                        </Typography>
                    </Box>
                </Box>
            )}

            {isHub && (
                <Box 
                    sx={{ 
                        p: 2.5, 
                        mb: 3, 
                        borderRadius: 3, 
                        bgcolor: alpha(theme.palette.success.main, 0.05),
                        border: `1px solid ${alpha(theme.palette.success.main, 0.25)}`,
                        backdropFilter: "blur(10px)",
                        display: 'flex', 
                        flexDirection: isMobile ? 'column' : 'row',
                        alignItems: isMobile ? 'flex-start' : 'center', 
                        gap: 2,
                        boxShadow: `0 4px 20px ${alpha(theme.palette.success.main, 0.05)}`
                    }}
                >
                    <GlobalIcon color="success" sx={{ fontSize: 28 }} />
                    <Box>
                        <Typography variant="subtitle2" sx={{ fontWeight: 700, letterSpacing: -0.2, color: theme.palette.success.main }}>
                            CENTRAL IDENTITY COMMAND ACTIVE (HUB MODE)
                        </Typography>
                        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.25, fontWeight: 500 }}>
                            You are in Hub Mode. All user accounts, roles, and security policies configured here are automatically synchronized to federated Nodes that have User & Identity Governance enabled.
                        </Typography>
                    </Box>
                </Box>
            )}


            <Paper sx={{
                p: 2,
                mb: 3,
                display: 'flex',
                flexDirection: isMobile ? 'column' : 'row',
                alignItems: isMobile ? 'stretch' : 'center',
                gap: 2,
                borderRadius: 2,
                backgroundColor: alpha(theme.palette.background.paper, 0.8),
                backdropFilter: 'blur(8px)',
                border: `1px solid ${alpha(theme.palette.divider, 0.1)}`
            }}>
                <TextField
                    size="small"
                    placeholder="Search users..."
                    value={searchTerm}
                    onChange={(e) => setSearchTerm(e.target.value)}
                    sx={{ flexGrow: 1, maxWidth: isMobile ? '100%' : 400 }}
                    InputProps={{
                        startAdornment: (
                            <InputAdornment position="start">
                                <SearchIcon color="action" />
                            </InputAdornment>
                        ),
                    }}
                />

                {!isMobile && <Box sx={{ flexGrow: 1 }} />}

                <Stack
                    direction={isMobile ? 'column' : 'row'}
                    spacing={1}
                    sx={{ width: isMobile ? '100%' : 'auto' }}
                >
                <input
                    type="file"
                    accept=".csv"
                    style={{ display: 'none' }}
                    ref={fileInputRef}
                    onChange={handleImportCSV}
                />

                <Tooltip title={isNode ? "Import locked by Hub" : "Import from CSV"}>
                    <span>
                        <Button
                            startIcon={<UploadIcon />}
                            onClick={() => fileInputRef.current?.click()}
                            variant="outlined"
                            disabled={isNode}
                            fullWidth={isMobile}
                        >
                            Import
                        </Button>
                    </span>
                </Tooltip>

                <Tooltip title="Export to CSV">
                    <Button
                        startIcon={<DownloadIcon />}
                        onClick={handleExportCSV}
                        variant="outlined"
                        fullWidth={isMobile}
                    >
                        Export
                    </Button>
                </Tooltip>

                <Tooltip title={isNode ? "Creating users locked by Hub" : ""}>
                    <span>
                        <Button
                            variant="contained"
                            startIcon={<AddIcon />}
                            onClick={() => setOpenAdd(true)}
                            disabled={isNode}
                            fullWidth={isMobile}
                            sx={{
                                borderRadius: 2,
                                textTransform: 'none',
                                px: 3
                            }}
                        >
                            Add User
                        </Button>
                    </span>
                </Tooltip>
                </Stack>
            </Paper>

            {isMobile ? (
                <MobileUserList
                    users={sortedAndFilteredUsers}
                    loading={loading}
                    isHub={isHub}
                    isNode={isNode}
                    currentNodeId={currentNodeId}
                    currentUsername={user?.username}
                    onOpen={(u) => {
                        setSelectedUser(u);
                        setDrawerOpen(true);
                    }}
                    onReset={(u) => {
                        setSelectedUser(u);
                        setOpenReset(true);
                    }}
                    onDelete={handleDeleteUser}
                />
            ) : (
            <TableContainer component={Paper} sx={{ borderRadius: 2, overflow: 'hidden' }}>
                <Table>
                    <TableHead sx={{ backgroundColor: alpha(theme.palette.primary.main, 0.05) }}>
                        <TableRow>
                            <TableCell>
                                <TableSortLabel
                                    active={orderBy === 'username'}
                                    direction={orderBy === 'username' ? order : 'asc'}
                                    onClick={() => handleSort('username')}
                                >
                                    Username
                                </TableSortLabel>
                            </TableCell>
                            <TableCell>
                                <TableSortLabel
                                    active={orderBy === 'email'}
                                    direction={orderBy === 'email' ? order : 'asc'}
                                    onClick={() => handleSort('email')}
                                >
                                    Email
                                </TableSortLabel>
                            </TableCell>
                            <TableCell>
                                <TableSortLabel
                                    active={orderBy === 'role'}
                                    direction={orderBy === 'role' ? order : 'asc'}
                                    onClick={() => handleSort('role')}
                                >
                                    Role
                                </TableSortLabel>
                            </TableCell>
                            <TableCell>
                                <TableSortLabel
                                    active={orderBy === 'createdAt'}
                                    direction={orderBy === 'createdAt' ? order : 'asc'}
                                    onClick={() => handleSort('createdAt')}
                                >
                                    Created
                                </TableSortLabel>
                            </TableCell>
                            <TableCell>
                                <TableSortLabel
                                    active={orderBy === 'lastLoginAt'}
                                    direction={orderBy === 'lastLoginAt' ? order : 'asc'}
                                    onClick={() => handleSort('lastLoginAt')}
                                >
                                    Last Login
                                </TableSortLabel>
                            </TableCell>
                            <TableCell>Provider</TableCell>
                            <TableCell align="right">Actions</TableCell>
                        </TableRow>
                    </TableHead>
                    <TableBody>
                        {loading ? (
                            <TableRow>
                                <TableCell colSpan={6} align="center" sx={{ py: 8 }}>
                                    <CircularProgress size={32} />
                                    <Typography sx={{ mt: 2, color: 'text.secondary' }}>Loading users...</Typography>
                                </TableCell>
                            </TableRow>
                        ) : sortedAndFilteredUsers.length === 0 ? (
                            <TableRow>
                                <TableCell colSpan={6} align="center" sx={{ py: 8 }}>
                                    <Typography sx={{ color: 'text.secondary' }}>No users found matching your search.</Typography>
                                </TableCell>
                            </TableRow>
                        ) : sortedAndFilteredUsers.map((u) => (
                            <TableRow
                                key={u.id}
                                hover
                                onClick={() => {
                                    setSelectedUser(u);
                                    setDrawerOpen(true);
                                }}
                                sx={{ cursor: 'pointer', '&:last-child td, &:last-child th': { border: 0 } }}
                            >
                                <TableCell sx={{ fontWeight: 500 }}>{u.username}</TableCell>
                                <TableCell>{u.email || <Typography variant="caption" color="text.disabled">No email</Typography>}</TableCell>
                                <TableCell>
                                    <Chip
                                        label={u.role}
                                        size="small"
                                        color={getRoleChipColor(u.role)}
                                        icon={getRoleIcon(u.role) ?? undefined}
                                        sx={{ borderRadius: 1.5, fontWeight: 500 }}
                                    />
                                </TableCell>
                                <TableCell>{new Date(u.createdAt).toLocaleDateString()}</TableCell>
                                <TableCell>
                                    {u.lastLoginAt ? (
                                        <Tooltip title={new Date(u.lastLoginAt).toLocaleString()}>
                                            <Typography variant="body2">{new Date(u.lastLoginAt).toLocaleDateString()}</Typography>
                                        </Tooltip>
                                    ) : (
                                        <Typography variant="body2" color="text.disabled">Never</Typography>
                                    )}
                                </TableCell>
                                <TableCell>
                                    {(() => {
                                        if (isNode) {
                                            if (u.originNodeId) {
                                                const isFromThisNode = u.originNodeId === currentNodeId;
                                                return (
                                                    <Chip 
                                                        label={isFromThisNode ? `Local Node (Migrated)` : `Node: ${u.originNodeId}`} 
                                                        size="small" 
                                                        variant="outlined"
                                                        color="success"
                                                        sx={{ 
                                                            fontWeight: 700, 
                                                            fontSize: '0.65rem',
                                                            borderRadius: 1.5,
                                                            borderStyle: 'dashed'
                                                        }} 
                                                    />
                                                );
                                            } else {
                                                const prov = u.provider && u.provider !== 'Local' ? `Hub (${u.provider})` : 'Hub (Local)';
                                                return (
                                                    <Chip 
                                                        label={prov} 
                                                        size="small" 
                                                        variant="outlined"
                                                        color="primary"
                                                        sx={{ 
                                                            fontWeight: 700, 
                                                            fontSize: '0.65rem',
                                                            borderRadius: 1.5
                                                        }} 
                                                    />
                                                );
                                            }
                                        } else if (isHub) {
                                            if (u.originNodeId) {
                                                return (
                                                    <Chip 
                                                        label={`Node: ${u.originNodeId}`} 
                                                        size="small" 
                                                        variant="outlined"
                                                        color="success"
                                                        sx={{ 
                                                            fontWeight: 700, 
                                                            fontSize: '0.65rem',
                                                            borderRadius: 1.5,
                                                            borderStyle: 'dashed'
                                                        }} 
                                                    />
                                                );
                                            } else {
                                                const prov = u.provider && u.provider !== 'Local' ? `Hub (${u.provider})` : 'Hub (Local)';
                                                return (
                                                    <Chip 
                                                        label={prov} 
                                                        size="small" 
                                                        variant="outlined"
                                                        color="primary"
                                                        sx={{ 
                                                            fontWeight: 700, 
                                                            fontSize: '0.65rem',
                                                            borderRadius: 1.5
                                                        }} 
                                                    />
                                                );
                                            }
                                        } else {
                                            // Standalone Node mode (not governed)
                                            return (
                                                <Chip 
                                                    label={u.provider || 'Local'} 
                                                    size="small" 
                                                    variant="outlined"
                                                    sx={{ 
                                                        fontWeight: 700, 
                                                        fontSize: '0.65rem',
                                                        borderColor: u.provider === 'OIDC' ? 'primary.main' : u.provider === 'LDAP' ? 'info.main' : 'divider',
                                                        color: u.provider === 'OIDC' ? 'primary.main' : u.provider === 'LDAP' ? 'info.main' : 'text.secondary',
                                                        borderRadius: 1.5
                                                    }} 
                                                />
                                            );
                                        }
                                    })()}
                                </TableCell>
                                <TableCell align="right" onClick={(e) => e.stopPropagation()}>
                                    <Stack direction="row" spacing={1} justifyContent="flex-end">
                                        <Tooltip title={isNode ? "Managed globally by Hub" : (u.provider !== 'Local' && !!u.provider ? `Managed by ${u.provider}` : "Reset Password")}>
                                            <IconButton
                                                size="small"
                                                color="warning"
                                                onClick={() => {
                                                    setSelectedUser(u);
                                                    setOpenReset(true);
                                                }}
                                                disabled={isNode || (u.provider !== 'Local' && !!u.provider)}
                                                sx={{ opacity: (isNode || (u.provider !== 'Local' && !!u.provider)) ? 0.3 : 1 }}
                                            >
                                                <PasswordIcon fontSize="small" />
                                            </IconButton>
                                        </Tooltip>
                                        <Tooltip title={isNode ? "Managed globally by Hub" : "Delete User"}>
                                            <IconButton
                                                size="small"
                                                onClick={() => handleDeleteUser(u.id)}
                                                disabled={isNode || u.username === user?.username}
                                                sx={{ opacity: (isNode || u.username === user?.username) ? 0.3 : 1 }}
                                            >
                                                <DeleteIcon fontSize="small" />
                                            </IconButton>
                                        </Tooltip>
                                    </Stack>
                                </TableCell>
                            </TableRow>
                        ))}
                    </TableBody>
                </Table>
            </TableContainer>
            )}


            {/* Add User Dialog */}
            <Dialog open={openAdd} onClose={() => setOpenAdd(false)} maxWidth="xs" fullWidth fullScreen={isMobile}>
                <DialogTitle>Add New User</DialogTitle>
                <DialogContent>
                    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
                        <TextField
                            label="Username"
                            fullWidth
                            value={newUsername}
                            onChange={(e) => setNewUsername(e.target.value)}
                        />
                        {newRole !== "AlertOnly" && (
                            <TextField
                                label="Password"
                                type="password"
                                fullWidth
                                value={newPassword}
                                onChange={(e) => setNewPassword(e.target.value)}
                            />
                        )}
                        <TextField
                            label={newRole === "AlertOnly" ? "Email (Required for Alerts)" : "Email (Optional)"}
                            fullWidth
                            value={newEmail}
                            onChange={(e) => setNewEmail(e.target.value)}
                            error={newRole === "AlertOnly" && !newEmail}
                        />
                        <FormControl fullWidth>
                            <InputLabel>Role</InputLabel>
                            <Select
                                value={newRole}
                                label="Role"
                                onChange={(e) => setNewRole(e.target.value)}
                            >
                                <MenuItem value="Admin">Admin</MenuItem>
                                <MenuItem value="Standard">Standard</MenuItem>
                                <MenuItem value="Viewer">Viewer</MenuItem>
                                <MenuItem value="AlertOnly">Alert Only</MenuItem>
                            </Select>
                        </FormControl>

                        <Button
                            variant="text"
                            size="small"
                            onClick={downloadSampleCSV}
                            sx={{ alignSelf: 'flex-start' }}
                        >
                            Download Import Template
                        </Button>
                    </Box>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setOpenAdd(false)}>Cancel</Button>
                    <Button
                        variant="contained"
                        onClick={handleCreateUser}
                        disabled={
                            !newUsername ||
                            (newRole !== "AlertOnly" && !newPassword) ||
                            (newRole === "AlertOnly" && !newEmail)
                        }
                    >
                        Create
                    </Button>
                </DialogActions>
            </Dialog>

            {/* Reset Password Dialog */}
            <Dialog open={openReset} onClose={() => setOpenReset(false)} maxWidth="xs" fullWidth fullScreen={isMobile}>
                <DialogTitle>Reset Password for {selectedUser?.username}</DialogTitle>
                <DialogContent>
                    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
                        <Typography variant="body2" color="text.secondary">
                            Enter a new password for this user. They will need to use this to log in immediately.
                        </Typography>
                        <TextField
                            label="New Password"
                            type="password"
                            fullWidth
                            autoFocus
                            value={resetPassword}
                            onChange={(e) => setResetPassword(e.target.value)}
                        />
                    </Box>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setOpenReset(false)}>Cancel</Button>
                    <Button variant="contained" color="warning" onClick={handleResetPassword} disabled={!resetPassword}>Reset Password</Button>
                </DialogActions>
            </Dialog>

            <UserDrawer
                open={drawerOpen}
                onClose={() => setDrawerOpen(false)}
                user={selectedUser}
                onUpdate={loadUsers}
                isNode={isNode}
            />


            <Snackbar
                open={snackbar.open}
                autoHideDuration={6000}
                onClose={() => setSnackbar({ ...snackbar, open: false })}
                anchorOrigin={{ vertical: 'bottom', horizontal: isMobile ? 'center' : 'right' }}
            >
                <Alert severity={snackbar.severity} sx={{ width: '100%' }}>
                    {snackbar.message}
                </Alert>
            </Snackbar>
        </PageShell>
    );
}
