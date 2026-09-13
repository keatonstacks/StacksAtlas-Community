using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Services.Audit;

namespace StacksAtlas.API.Services;

public class AuditService(
    IAuditEventRepository repository,
    IHttpContextAccessor httpContextAccessor,
    IClock clock,
    FederationSettingsStore federationSettings,
    SystemSettingsStore systemSettings,
    IAuditSyslogForwarder syslogForwarder) : IAuditService
{
    private readonly IAuditEventRepository _repository = repository;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly IClock _clock = clock;
    private readonly FederationSettingsStore _federationSettings = federationSettings;
    private readonly SystemSettingsStore _systemSettings = systemSettings;
    private readonly IAuditSyslogForwarder _syslogForwarder = syslogForwarder;

    public void Record(
        string action,
        string resourceType,
        string? resourceId,
        string outcome,
        string? detail = null,
        ClaimsPrincipal? actor = null,
        string? clientIp = null,
        string? actorUsernameOverride = null,
        string? actorUserIdOverride = null,
        string? actorRoleOverride = null)
    {
        actor ??= _httpContextAccessor.HttpContext?.User;
        clientIp ??= _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

        // Never leave required columns null (anonymous / failed-login actors use empty user id).
        var userId = AuditActorResolver.ResolveUserId(actor, actorUserIdOverride);
        var username = AuditActorResolver.ResolveUsername(actor, actorUsernameOverride);
        var role = AuditActorResolver.ResolveRole(actor, actorRoleOverride);

        var fed = _federationSettings.Current;
        var siteName = _systemSettings.Current.Onboarding?.SiteName;
        if (string.IsNullOrWhiteSpace(siteName))
            siteName = fed.NodeDisplayName;

        var auditEvent = new AuditEvent
        {
            TimestampUtc = _clock.UtcNow,
            Action = action ?? string.Empty,
            ActorUserId = userId ?? string.Empty,
            ActorUsername = string.IsNullOrWhiteSpace(username) ? "unknown" : username,
            ActorRole = role ?? string.Empty,
            ResourceType = resourceType ?? string.Empty,
            ResourceId = resourceId,
            Outcome = string.IsNullOrWhiteSpace(outcome) ? AuditOutcomes.Success : outcome,
            ClientIp = clientIp,
            Detail = detail,
            NodeId = ExecutionState.IsHub ? null : fed.NodeId,
            NodeName = siteName,
        };

        _repository.Append(auditEvent);
        _syslogForwarder.Forward(auditEvent);
    }
}
