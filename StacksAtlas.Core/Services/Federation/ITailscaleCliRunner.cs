namespace StacksAtlas.Core.Services.Federation;

public interface ITailscaleCliRunner
{
    bool IsCliAvailable();
    Task<(bool Success, string Output, string? Error)> RunAsync(string arguments, CancellationToken cancellationToken = default);
}
