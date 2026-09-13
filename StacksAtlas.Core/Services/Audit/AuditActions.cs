namespace StacksAtlas.Core.Services.Audit;

public static class AuditActions
{
    public const string AuthLoginSuccess = "auth.login.success";
    public const string AuthLoginFailed = "auth.login.failed";
    public const string AuthSetup = "auth.setup";
    public const string AuthSsoLogin = "auth.sso.login";

    public const string UserCreate = "user.create";
    public const string UserUpdate = "user.update";
    public const string UserDelete = "user.delete";
    public const string UserPasswordReset = "user.password.reset";

    public const string ApiTokenCreate = "api_token.create";
    public const string ApiTokenRevoke = "api_token.revoke";

    public const string LicenseActivate = "license.activate";

    public const string SettingsUpdate = "settings.update";
    public const string SettingsAuthUpdate = "settings.auth.update";
    public const string SettingsPortsUpdate = "settings.ports.update";
    public const string SettingsLoggingToggle = "settings.logging.toggle";
    public const string SettingsNetworkUpdate = "settings.network.update";
    public const string SettingsPollingUpdate = "settings.polling.update";
    public const string SettingsEmailUpdate = "settings.email.update";
    public const string SettingsOpenAvcUpdate = "settings.openavc.update";

    public const string WebhookCreate = "webhook.create";
    public const string WebhookUpdate = "webhook.update";
    public const string WebhookDelete = "webhook.delete";

    public const string FederationEnroll = "federation.enroll";
    public const string FederationDecouple = "federation.decouple";
    public const string FederationNodeDelete = "federation.node.delete";
    public const string FederationNodeUpdate = "federation.node.update";
    public const string FederationSiteReset = "federation.site.reset";
    public const string FederationSettingsUpdate = "federation.settings.update";

    public const string DeviceArchive = "device.archive";
    public const string DeviceRemoveFromFleet = "device.remove_from_fleet";
    public const string DeviceRestore = "device.restore";
    public const string DeviceRestoreToFleet = "device.restore_to_fleet";
    public const string DeviceRiskAcknowledge = "device.risk.acknowledge";
    public const string DeviceAttachmentUpdate = "device.attachment.update";

    public const string SnapshotCreate = "snapshot.create";
    public const string SnapshotRestore = "snapshot.restore";
    public const string SnapshotDelete = "snapshot.delete";
    public const string SnapshotFreshStart = "snapshot.fresh_start";

    public const string SecurityKeyRotate = "security.key.rotate";
    public const string SecurityPortableExport = "security.portable.export";
    public const string SecurityPortableImport = "security.portable.import";

    public const string DatabaseApply = "database.apply";

    public const string UpdatesFleetNotify = "updates.fleet.notify";
    public const string UpdatesFleetTrigger = "updates.fleet.trigger";
    public const string UpdatesScheduleUpdate = "updates.schedule.update";
    public const string UpdatesScheduleApply = "updates.schedule.apply";
}

public static class AuditOutcomes
{
    public const string Success = "Success";
    public const string Denied = "Denied";
    public const string Failed = "Failed";
}
