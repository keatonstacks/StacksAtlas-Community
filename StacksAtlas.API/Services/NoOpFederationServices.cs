using StacksAtlas.API.Services;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Services.Scanning;
using System.Security.Claims;
using StacksAtlas.Core.Services.Network;

namespace StacksAtlas.API.Services;

/// <summary>
/// A No-Op AlertService for Node mode. Nodes do not evaluate or send alerts.
/// </summary>
public class NoOpAlertService : IAlertService
{
    public Task EvaluateAndSendAlertsAsync(List<Device> currentDevices, Dictionary<Guid, string> preSweepStatuses) 
        => Task.CompletedTask;

    public Task DelegateAlertAsync(AlertEventType alertType, Device device, string nodeId)
        => Task.CompletedTask;
}

/// <summary>
/// A No-Op AuthService for Node mode.
/// Implements IAuthService directly to avoid constructor chain issues.
/// </summary>
public class NoOpAuthService : IAuthService
{
    public bool AnyUsers() => true;
    public List<User> GetAllUsers() => new();
    public User? GetUserById(Guid id) => null;
    public bool UserExists(string username) => false;
    public User? RegisterAdmin(string username, string password) => null;
    public User? CreateUser(string u, string? p, string r, string? e = null, string pr = "Local", string? x = null) => null;
    public User GetOrCreateExternalUser(string p, string x, string u, string? e, List<string>? g = null) => null!;
    public bool DeleteUser(Guid id) => false;
    public bool UpdateUser(Guid id, string? e, string? r) => false;
    public User? ValidateUser(string u, string p) => null;
    public bool ResetUserPassword(Guid i, string n) => false;
    public void ForceResetPassword(string u, string n) { }
    public bool UpdateAlertPreferences(Guid i, string? e, bool en, bool d, bool up, bool n, string s, bool w = false, Guid? p = null, List<Guid>? ps = null) => false;
    public string GenerateToken(User u, int e = 7) => string.Empty;
    public ClaimsPrincipal? GetPrincipalFromToken(string t, bool v = true) => null;
}

/// <summary>
/// A No-Op DeepScanService for Hub mode.
/// </summary>
public class NoOpDeepScanService : IDeepScanService
{
    public void QueueScan(Guid deviceId) { }
    public (string Status, int Progress) GetScanStatus(Guid deviceId) => ("idle", 0);
    public bool IsNmapAvailable() => false;
}

/// <summary>
/// A No-Op HostPinger for Hub mode.
/// </summary>
public class NoOpHostPinger : IHostPinger
{
    public Task<HostScanResult> PingAsync(string ip, CancellationToken token, bool onDemand = false) 
        => Task.FromResult(HostScanResult.Offline(ip));
}

/// <summary>
/// A No-Op WakeOnLanService for Hub mode.
/// </summary>
public class NoOpWakeOnLanService : IWakeOnLanService
{
    public Task SendMagicPacketAsync(string macAddress) => Task.CompletedTask;
}
