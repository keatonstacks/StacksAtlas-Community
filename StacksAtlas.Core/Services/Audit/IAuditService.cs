using System.Security.Claims;

namespace StacksAtlas.Core.Services.Audit;

public interface IAuditService
{
    void Record(
        string action,
        string resourceType,
        string? resourceId,
        string outcome,
        string? detail = null,
        ClaimsPrincipal? actor = null,
        string? clientIp = null,
        string? actorUsernameOverride = null,
        string? actorUserIdOverride = null,
        string? actorRoleOverride = null);
}
