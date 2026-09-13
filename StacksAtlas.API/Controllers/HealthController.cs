using LiteDB;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;
using System.Reflection;

namespace StacksAtlas.API.Controllers; // File-scoped namespace (IDE0161)

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class HealthController(
    CleanupSettingsStore cleanupStore,
    SystemSettingsStore settingsStore,
    LiteDatabase db,
    string databasePath) : ControllerBase
{
    private readonly CleanupOptions _cleanupOptions = cleanupStore.Current;
    private readonly LiteDatabase _db = db;
    private readonly string _databasePath = databasePath;

    [HttpGet("database")]
    public ActionResult<DatabaseHealth> GetDatabaseHealth()
    {
        var metrics = DatabaseStorageMetrics.Read(_databasePath);
        var fleetProvider = settingsStore.Current.Database?.Provider ?? "Sqlite";

        var col = _db.GetCollection<MaintenanceState>("maintenance");
        var state = col.FindById(1) ?? new MaintenanceState();

        return Ok(new DatabaseHealth
        {
            SizeBytes = metrics.ApplianceSizeBytes,
            ApplianceSizeBytes = metrics.ApplianceSizeBytes,
            FleetSizeBytes = metrics.FleetSizeBytes,
            HasFleetDatabase = metrics.HasFleetDatabase,
            ApplianceEngine = "LiteDB",
            FleetEngine = metrics.HasFleetDatabase && ExecutionState.IsHub
                ? DatabaseStorageMetrics.FormatFleetEngine(fleetProvider)
                : null,
            RetentionDays = _cleanupOptions.AutoArchiveDays,
            CleanupIntervalMinutes = _cleanupOptions.RunEveryMinutes,
            LastCleanupUtc = state.LastCleanupUtc,
            NextCleanupUtc = state.NextCleanupUtc,
            LastCompactUtc = state.LastCompactUtc,
            NextCompactUtc = state.NextCompactUtc,
            LastPurgedCount = state.LastPurgedCount,
            CurrentStatus = state.CurrentStatus,
            LastErrorMessage = state.LastErrorMessage
        });
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult GetHealth()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    [HttpGet("version")]
    public IActionResult GetVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";
        // Strip the commit hash off the end of informational version if present
        version = version.Split('+')[0];
        return Ok(new { version = version, build = "StacksAtlas Engine" });
    }
}
