using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Services.Governance;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Services.Audit;
using System.IO;

namespace StacksAtlas.API.Controllers;

[Authorize]
[ApiController]
[Route("api/database")]
public class DatabaseController : ControllerBase
{
    private readonly SystemSettingsStore _settingsStore;
    private readonly SettingsEncryptor _encryptor;
    private readonly DatabaseInfrastructureMigrationService? _migrationService;
    private readonly string _databasePath;
    private readonly IAuditService _audit;
    private readonly ILogger<DatabaseController> _logger;

    public DatabaseController(
        SystemSettingsStore settingsStore,
        SettingsEncryptor encryptor,
        IServiceProvider serviceProvider,
        string databasePath,
        IAuditService audit,
        ILogger<DatabaseController> logger)
    {
        _settingsStore = settingsStore;
        _encryptor = encryptor;
        _migrationService = serviceProvider.GetService<DatabaseInfrastructureMigrationService>();
        _databasePath = databasePath;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var settings = _settingsStore.Current.Database;
        var provider = settings?.Provider ?? "Sqlite";
        var connectionString = settings?.ConnectionString ?? string.Empty;

        // Mask/Censor connection string if defined
        var maskedConnectionString = MaskConnectionString(connectionString);
        var metrics = DatabaseStorageMetrics.Read(_databasePath);
        var isHub = ExecutionState.IsHub;
        var fleetProvider = settings?.Provider ?? "Sqlite";
        var fleetEngine = metrics.HasFleetDatabase && isHub
            ? DatabaseStorageMetrics.FormatFleetEngine(fleetProvider)
            : null;

        return Ok(new DatabaseStatusResponse
        {
            IsHub = isHub,
            ApplianceEngine = "LiteDB",
            FleetEngine = fleetEngine,
            FleetProvider = fleetProvider,
            CanConfigureFleetEngine = isHub,
            Provider = isHub ? fleetProvider : "LiteDB",
            ConnectionString = maskedConnectionString,
            DatabaseSize = metrics.ApplianceSizeBytes,
            ApplianceDatabaseSize = metrics.ApplianceSizeBytes,
            FleetDatabaseSize = metrics.FleetSizeBytes,
            HasFleetDatabase = metrics.HasFleetDatabase,
            ApplianceFileName = metrics.ApplianceFileName,
            FleetFileName = metrics.FleetFileName
        });
    }

    [HttpPost("test")]
    public async Task<IActionResult> TestConnection([FromBody] DatabaseConfigRequest request)
    {
        if (_migrationService == null)
        {
            return BadRequest(new { Success = false, Message = "Fleet database configuration is only available on Hub appliances." });
        }

        if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            return BadRequest("Provider and connection string are required fields.");
        }

        var isConnected = await _migrationService.TestConnectionAsync(request.Provider, request.ConnectionString);
        if (isConnected)
        {
            return Ok(new { Success = true, Message = "Connection verified successfully!" });
        }

        return StatusCode(500, new { Success = false, Message = "Failed to connect to database. Check credentials, hostname, or port." });
    }

    [HttpPost("apply")]
    public async Task<IActionResult> ApplyDatabaseSettings([FromBody] DatabaseApplyRequest request)
    {
        if (_migrationService == null)
        {
            return BadRequest(new { Success = false, Message = "Fleet database configuration is only available on Hub appliances." });
        }

        if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            return BadRequest("Provider and connection string are required fields.");
        }

        try
        {
            await _migrationService.MigrateDatabaseAsync(request.Provider, request.ConnectionString, request.MigrateData);
            _audit.Record(
                AuditActions.DatabaseApply,
                "database",
                request.Provider,
                AuditOutcomes.Success,
                detail: $"migrateData={request.MigrateData}");
            return Ok(new { Success = true, Message = "Database migrated and settings applied successfully!" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply database migration settings.");
            return StatusCode(500, new { Success = false, Message = ex.Message });
        }
    }

    private string MaskConnectionString(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // Decrypt first if it is encrypted in settings
        var rawValue = value;
        if (_encryptor.IsEncrypted(value))
        {
            rawValue = _encryptor.Unprotect(value);
        }

        // Censor passwords using regex/string parsing securely
        if (rawValue.Contains("Password=", StringComparison.OrdinalIgnoreCase))
        {
            // Simple robust masking of password tokens in RDBMS strings
            return System.Text.RegularExpressions.Regex.Replace(
                rawValue, 
                @"(Password|pwd|pwd_hash)=([^;]+)", 
                "$1=********", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return rawValue;
    }
}

public class DatabaseStatusResponse
{
    public bool IsHub { get; set; }
    /// <summary>Always LiteDB  -  local appliance store (StacksAtlas.db).</summary>
    public string ApplianceEngine { get; set; } = "LiteDB";
    /// <summary>Fleet relational engine when Hub brain is present.</summary>
    public string? FleetEngine { get; set; }
    public string FleetProvider { get; set; } = "Sqlite";
    public bool CanConfigureFleetEngine { get; set; }
    /// <summary>Legacy  -  appliance engine on nodes; fleet provider on Hub.</summary>
    public string Provider { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public long DatabaseSize { get; set; }
    public long ApplianceDatabaseSize { get; set; }
    public long FleetDatabaseSize { get; set; }
    public bool HasFleetDatabase { get; set; }
    public string ApplianceFileName { get; set; } = "StacksAtlas.db";
    public string FleetFileName { get; set; } = FleetDatabaseGovernance.HubFileName;
}

public class DatabaseConfigRequest
{
    public string Provider { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
}

public class DatabaseApplyRequest
{
    public string Provider { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public bool MigrateData { get; set; } = true;
}
