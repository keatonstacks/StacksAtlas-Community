using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Abstractions;

public interface IAuditEventRepository
{
    void Append(AuditEvent auditEvent);

    IReadOnlyList<AuditEvent> Query(
        int skip,
        int take,
        string? actionPrefix,
        string[]? nodeIds,
        DateTime? sinceUtc = null,
        DateTime? untilUtc = null,
        bool hubOnly = false);
}
