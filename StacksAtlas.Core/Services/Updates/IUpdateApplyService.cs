using StacksAtlas.Core.Models.Updates;

namespace StacksAtlas.Core.Services.Updates;

public interface IUpdateApplyService
{
    Task<UpdateApplyResult> ApplyAsync(string? channel = null, CancellationToken cancellationToken = default);
}
