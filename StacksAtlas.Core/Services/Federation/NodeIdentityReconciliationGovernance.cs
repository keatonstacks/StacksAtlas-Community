using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Federation;

public static class NodeIdentityReconciliationGovernance
{
    public static bool IsPriorNodeOnline(FederatedNode prior) =>
        !string.IsNullOrWhiteSpace(prior.ConnectionId);

    public static void MergeRetiredMetadata(FederatedNode canonical, FederatedNode retired)
    {
        if (IsGenericNodeName(canonical.Name) && !IsGenericNodeName(retired.Name))
            canonical.Name = retired.Name;

        if (string.IsNullOrWhiteSpace(canonical.Client))
            canonical.Client = retired.Client;
        if (string.IsNullOrWhiteSpace(canonical.Building))
            canonical.Building = retired.Building;
        if (string.IsNullOrWhiteSpace(canonical.Room))
            canonical.Room = retired.Room;

        if (!canonical.SyncUsers && retired.SyncUsers)
            canonical.SyncUsers = retired.SyncUsers;
        if (!canonical.SyncUserRegistry && retired.SyncUserRegistry)
            canonical.SyncUserRegistry = retired.SyncUserRegistry;
        if (!canonical.SyncSsoSettings && retired.SyncSsoSettings)
            canonical.SyncSsoSettings = retired.SyncSsoSettings;
        if (!canonical.SyncAlertSettings && retired.SyncAlertSettings)
            canonical.SyncAlertSettings = retired.SyncAlertSettings;
        if (!canonical.SyncSiemSettings && retired.SyncSiemSettings)
            canonical.SyncSiemSettings = retired.SyncSiemSettings;
        if (!canonical.DelegateAlertDispatch && retired.DelegateAlertDispatch)
            canonical.DelegateAlertDispatch = retired.DelegateAlertDispatch;
        if (canonical.IsIdentityImported == false && retired.IsIdentityImported)
            canonical.IsIdentityImported = retired.IsIdentityImported;

        if (string.IsNullOrWhiteSpace(canonical.ScanSettingsJson) && !string.IsNullOrWhiteSpace(retired.ScanSettingsJson))
            canonical.ScanSettingsJson = retired.ScanSettingsJson;
    }

    public static void MergeStricterLifecycle(Device winner, Device loser)
    {
        if (loser.IsPermanentlyRemoved && !winner.IsPermanentlyRemoved)
        {
            winner.IsPermanentlyRemoved = true;
            winner.RemovedUtc = loser.RemovedUtc ?? winner.RemovedUtc;
            winner.RemovedBy = loser.RemovedBy ?? winner.RemovedBy;
            winner.RemovedReason = loser.RemovedReason ?? winner.RemovedReason;
        }

        if (loser.IsDeleted && !winner.IsDeleted)
            winner.IsDeleted = true;
    }

    private static bool IsGenericNodeName(string? name) =>
        string.IsNullOrWhiteSpace(name)
        || name.StartsWith("Node-", StringComparison.OrdinalIgnoreCase);
}
