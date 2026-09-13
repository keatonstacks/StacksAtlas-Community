using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Data;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Services.Audit;

namespace StacksAtlas.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UsersController(
    IAuthService auth, 
    IAlertEventRepository alertEvents, 
    ApiKeyService apiKeys, 
    WebhookRepository webhookRepo,
    IDeviceRepository deviceRepo,
    IFederatedNodeRepository nodeRepo,
    IFederationIdentitySyncService identitySync,
    IAuditService audit) : ControllerBase
{
    private readonly IAuthService _auth = auth;
    private readonly IAlertEventRepository _alertEvents = alertEvents;
    private readonly ApiKeyService _apiKeys = apiKeys;
    private readonly WebhookRepository _webhookRepo = webhookRepo;
    private readonly IDeviceRepository _deviceRepo = deviceRepo;
    private readonly IFederatedNodeRepository _nodeRepo = nodeRepo;
    private readonly IFederationIdentitySyncService _identitySync = identitySync;
    private readonly IAuditService _audit = audit;

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public IActionResult GetAll()
    {
        var users = _auth.GetAllUsers();
        // Return sanitized view (don't send Hashes/Salts to frontend)
        return Ok(users.Select(u => new {
            u.Id,
            u.Username,
            u.Email,
            u.Role,
            u.Provider,
            u.IsActive,
            u.CreatedAt,
            u.LastLoginAt,
            u.OriginNodeId,
            // Alert Preferences
            u.AlertEmail,
            u.AlertsEnabled,
            u.AlertOnDeviceDown,
            u.AlertOnDeviceUp,
            u.AlertOnNewDevice,
            u.AlertSeverity,
            u.WebhookEnabled,
            u.PreferredWebhookId,
            u.PreferredWebhookIds
        }));
    }

    [HttpGet("me")]
    public IActionResult GetMe()
    {
        var currentUserIdStr = User.FindFirst("id")?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId))
            return Unauthorized("Invalid user identification in token.");

        var user = _auth.GetUserById(currentUserId);
        if (user == null) return NotFound();

        return Ok(new {
            user.Id,
            user.Username,
            user.Email,
            user.Role,
            user.Provider,
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
            user.OriginNodeId,
            // Alert Preferences
            user.AlertEmail,
            user.AlertsEnabled,
            user.AlertOnDeviceDown,
            user.AlertOnDeviceUp,
            user.AlertOnNewDevice,
            user.AlertSeverity,
            user.WebhookEnabled,
            user.PreferredWebhookId,
            user.PreferredWebhookIds
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var role = request.Role ?? "Standard";

        // Logic for AlertOnly users: No password required, but Email IS required.
        if (role == "AlertOnly")
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email))
                return BadRequest("Username and Email are required for AlertOnly accounts.");
        }
        else
        {
            // Standard/Admin/Viewer: Password IS required.
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("Username and Password are required.");
        }

        var user = _auth.CreateUser(request.Username, request.Password, role, request.Email);
        
        if (user == null)
            return BadRequest("User already exists or could not be created.");

        _audit.Record(AuditActions.UserCreate, "user", user.Id.ToString(), AuditOutcomes.Success, detail: $"role={user.Role}");

        await _identitySync.BroadcastIdentityStateAsync();

        return Ok(new {
            user.Id,
            user.Username,
            user.Email,
            user.Role,
            user.IsActive,
            user.CreatedAt
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        // Don't let users delete themselves (optional, but safer)
        var currentUserId = User.FindFirst("id")?.Value;
        if (currentUserId == id.ToString())
            return BadRequest("You cannot delete your own account.");

        var target = _auth.GetUserById(id);
        var success = _auth.DeleteUser(id);
        if (!success) return NotFound();

        _audit.Record(
            AuditActions.UserDelete,
            "user",
            id.ToString(),
            AuditOutcomes.Success,
            detail: target != null ? $"username={target.Username}" : null);

        await _identitySync.BroadcastIdentityStateAsync();

        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        var success = _auth.UpdateUser(id, request.Email, request.Role);
        if (!success) return NotFound();

        _audit.Record(
            AuditActions.UserUpdate,
            "user",
            id.ToString(),
            AuditOutcomes.Success,
            detail: request.Role != null ? $"role={request.Role}" : "profile updated");

        await _identitySync.BroadcastIdentityStateAsync();

        return Ok(new { message = "User updated successfully" });
    }

    [Authorize(Roles = "Admin")]
    [HttpPatch("{id}/password")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest("New password is required.");

        var success = _auth.ResetUserPassword(id, request.NewPassword);
        if (!success) return NotFound();

        _audit.Record(AuditActions.UserPasswordReset, "user", id.ToString(), AuditOutcomes.Success);

        await _identitySync.BroadcastIdentityStateAsync();

        return Ok(new { message = "Password successfully reset." });
    }

    [HttpPatch("{id}/alerts")]
    public async Task<IActionResult> UpdateAlertPreferences(Guid id, [FromBody] AlertPreferencesRequest request)
    {
        var success = _auth.UpdateAlertPreferences(
            id,
            request.AlertEmail,
            request.AlertsEnabled,
            request.AlertOnDeviceDown,
            request.AlertOnDeviceUp,
            request.AlertOnNewDevice,
            request.AlertSeverity ?? "All",
            request.WebhookEnabled,
            request.PreferredWebhookId,
            request.PreferredWebhookIds
        );
        
        if (!success) return NotFound();

        await _identitySync.BroadcastIdentityStateAsync();

        return Ok(new { message = "Alert preferences updated successfully" });
    }

    [HttpGet("{id}/alerts")]
    public IActionResult GetAlertPreferences(Guid id)
    {
        var user = _auth.GetUserById(id);
        if (user == null) return NotFound();

        var systemWebhooks = _webhookRepo.GetActive();
        var hasGlobalAlerts = systemWebhooks.Any(w => w.AlertSeverity == "All");

        return Ok(new {
            user.AlertEmail,
            user.AlertsEnabled,
            user.AlertOnDeviceDown,
            user.AlertOnDeviceUp,
            user.AlertOnNewDevice,
            user.AlertSeverity,
            user.WebhookEnabled,
            user.PreferredWebhookId,
            user.PreferredWebhookIds,
            SystemWideWebhooksActive = hasGlobalAlerts
        });
    }

    [Authorize(Roles = "Admin,Viewer")]
    [HttpGet("alerts/history")]
    public IActionResult GetAlertHistory([FromQuery] int limit = 50, [FromQuery(Name = "nodeId")] string[]? nodeIds = null)
    {
        var alerts = _alertEvents.GetRecentAlerts(limit, nodeIds);
        var devices = _deviceRepo.GetAll();
        var nodes = _nodeRepo.GetAll();

        return Ok(alerts.Select(a => {
            var device = devices.FirstOrDefault(d => d.Id.ToString() == a.DeviceId || d.IpAddress == a.DeviceIp);
            var nodeName = "Local Hub";
            var client = a.Client;
            var building = a.Building;
            var room = a.Room;

            if (device != null)
            {
                client ??= device.Client;
                building ??= device.Building;
                room ??= device.Room;
                if (!string.IsNullOrEmpty(device.NodeId))
                {
                    var node = nodes.FirstOrDefault(n => n.Id == device.NodeId);
                    if (node != null)
                    {
                        nodeName = node.Name;
                        client ??= node.Client;
                        building ??= node.Building;
                        room ??= node.Room;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(a.NodeId))
            {
                var node = nodes.FirstOrDefault(n => n.Id == a.NodeId);
                if (node != null)
                {
                    nodeName = node.Name;
                    client ??= node.Client;
                    building ??= node.Building;
                    room ??= node.Room;
                }
                else if (!string.IsNullOrEmpty(a.NodeName))
                {
                    nodeName = a.NodeName;
                }
            }
            else if (a.AlertType == AlertEventType.NodeDecoupled)
            {
                nodeName = a.NodeName ?? a.DeviceName;
                client ??= a.Client;
                building ??= a.Building;
                room ??= a.Room;
            }

            return new {
                a.Id,
                a.TriggeredAt,
                a.DeviceId,
                a.DeviceName,
                DeviceIp = NetworkHelper.NormalizeIpAddress(a.DeviceIp),
                AlertType = a.AlertType.ToString(),
                a.SentToEmails,
                a.SentToWebhooks,
                a.Success,
                a.ErrorMessage,
                NodeId = device?.NodeId ?? a.NodeId,
                NodeName = nodeName,
                Client = client,
                Building = building,
                Room = room
            };
        }));
    }


    [Authorize(Roles = "Admin")]
    [HttpDelete("alerts/history/{id}")]
    public IActionResult DeleteAlert(string id)
    {
        var success = _alertEvents.DeleteAlert(id);
        if (!success) return NotFound();
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("me/keys")]
    public IActionResult GetApiKeys()
    {
        var currentUserIdStr = User.FindFirst("id")?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId))
            return Unauthorized("Invalid user identification in token.");

        var keys = _apiKeys.GetUserKeys(currentUserId);
        return Ok(keys.Select(k => new {
            k.Id,
            k.Label,
            k.KeyPrefix,
            k.CreatedAt,
            k.LastUsedAt,
            k.IsActive
        }));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("me/keys")]
    public IActionResult CreateApiKey([FromBody] CreateApiKeyRequest request)
    {
        var currentUserIdStr = User.FindFirst("id")?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId))
            return Unauthorized("Invalid user identification in token.");

        if (string.IsNullOrWhiteSpace(request.Label))
            return BadRequest("A label is required to create an API key.");

        var newKey = _apiKeys.CreateApiKey(currentUserId, request.Label);

        _audit.Record(
            AuditActions.ApiTokenCreate,
            "api_token",
            newKey.Id.ToString(),
            AuditOutcomes.Success,
            detail: $"label={request.Label}; prefix={newKey.Prefix}");
        
        return Ok(new { 
            id = newKey.Id,
            token = newKey.RawKey,
            prefix = newKey.Prefix 
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("me/keys/{keyId}")]
    public IActionResult RevokeApiKey(Guid keyId)
    {
        var currentUserIdStr = User.FindFirst("id")?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId))
            return Unauthorized("Invalid user identification in token.");

        var success = _apiKeys.RevokeApiKey(currentUserId, keyId);
        if (!success) return NotFound("Key not found or does not belong to you.");

        _audit.Record(AuditActions.ApiTokenRevoke, "api_token", keyId.ToString(), AuditOutcomes.Success);

        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("alerts/history")]
    public IActionResult ClearAlertHistory([FromQuery(Name = "nodeId")] string[]? nodeIds = null)
    {
        var count = _alertEvents.ClearHistory(nodeIds);
        return Ok(new { Count = count });
    }
}

public record CreateUserRequest(string Username, string Password, string? Role, string? Email);
public record UpdateUserRequest(string? Role, string? Email);
public record ResetPasswordRequest(string NewPassword);
public record AlertPreferencesRequest(
    string? AlertEmail,
    bool AlertsEnabled,
    bool AlertOnDeviceDown,
    bool AlertOnDeviceUp,
    bool AlertOnNewDevice,
    string? AlertSeverity,
    bool WebhookEnabled,
    Guid? PreferredWebhookId,
    List<Guid>? PreferredWebhookIds
);

public record CreateApiKeyRequest(string Label);
