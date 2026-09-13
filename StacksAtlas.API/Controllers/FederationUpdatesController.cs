using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Services.Updates;
using StacksAtlas.Core.State;

namespace StacksAtlas.API.Controllers;

/// <summary>
/// Hub mTLS endpoints for enrolled Nodes to check/download staged update packages.
/// Intended for Hub port 5002 (client certificate required).
/// </summary>
[ApiController]
[Route("api/federation/updates")]
public sealed class FederationUpdatesController(
    IFederatedNodeRepository nodeRepo,
    IUpdateDepotService depotService,
    HubDepotUpdateCheckService depotCheckService,
    ILogger<FederationUpdatesController> logger) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("check")]
    public IActionResult Check(
        [FromQuery] string? channel = null,
        [FromQuery] string? version = null,
        [FromQuery] string? artifactKey = null)
    {
        if (!ExecutionState.IsHub)
            return BadRequest("Federation update check is only available on Hub.");

        if (!FederatedNodeCertificateAuth.TryResolveEnrolledNode(HttpContext, nodeRepo, out var node, out var failure))
        {
            logger.LogWarning("Federation update check rejected: {Reason}", failure);
            return Unauthorized(new { message = failure });
        }

        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(artifactKey))
            return BadRequest(new { message = "Query parameters version and artifactKey are required." });

        var baseUrl = $"{Request.Scheme}://{Request.Host}/api/federation/updates/artifacts";
        var result = depotCheckService.CheckForNode(channel, version, artifactKey, baseUrl);
        logger.LogInformation(
            "Federation update check for Node {NodeId}: channel={Channel} status={Status} available={Available}",
            node.Id,
            result.Channel,
            result.Status,
            result.AvailableVersion);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("artifacts/{artifactKey}/{version}")]
    public IActionResult DownloadArtifact(string artifactKey, string version, [FromQuery] string? channel = null)
    {
        if (!ExecutionState.IsHub)
            return BadRequest("Federation update artifacts are only available on Hub.");

        if (!FederatedNodeCertificateAuth.TryResolveEnrolledNode(HttpContext, nodeRepo, out var node, out var failure))
        {
            logger.LogWarning("Federation artifact download rejected: {Reason}", failure);
            return Unauthorized(new { message = failure });
        }

        var resolvedChannel = UpdateDepotPaths.NormalizeChannel(channel);
        if (!depotService.TryResolveArtifactPath(resolvedChannel, version, artifactKey, out var path))
            return NotFound(new { message = "Staged artifact was not found in the Hub depot." });

        var contentType = ResolveContentType(artifactKey);
        logger.LogInformation(
            "Federation artifact download for Node {NodeId}: {ArtifactKey}/{Version}",
            node.Id,
            artifactKey,
            version);
        return PhysicalFile(path, contentType, enableRangeProcessing: true);
    }

    private static string ResolveContentType(string artifactKey) =>
        artifactKey switch
        {
            UpdateArtifactKeys.WinX64Msi => "application/octet-stream",
            UpdateArtifactKeys.WinX64Portable => "application/octet-stream",
            UpdateArtifactKeys.OsxUniversalDmg => "application/octet-stream",
            UpdateArtifactKeys.LinuxDocker => "application/json",
            _ => "application/octet-stream"
        };
}
