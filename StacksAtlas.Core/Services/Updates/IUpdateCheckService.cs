using StacksAtlas.Core.Models.Updates;

namespace StacksAtlas.Core.Services.Updates;

public interface IUpdateCheckService
{
    /// <summary>
    /// Fetches the configured channel manifest + detached signature, verifies trust, and compares semver.
    /// Sends no appliance inventory  -  only a GET to the sovereign CDN URLs in <see cref="Settings.UpdateSettings"/>.
    /// </summary>
    /// <param name="httpClientName">
    /// Optional <see cref="IHttpClientFactory"/> client name. Apply uses the long-timeout apply client.
    /// </param>
    Task<UpdateCheckResult> CheckForUpdatesAsync(
        string? channel = null,
        CancellationToken cancellationToken = default,
        string? httpClientName = null);
}
