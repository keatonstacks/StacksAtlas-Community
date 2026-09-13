using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Services.Governance;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Audit;

namespace StacksAtlas.API.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/[controller]")]
public class SecurityController(
    IDatabaseMigrationService migrationService, 
    ISnapshotService snapshotService,
    IDeviceRepository deviceRepo,
    SecurityAuditService securityAuditor,
    IAuditService audit) : ControllerBase
{
    private readonly IDatabaseMigrationService _migrationService = migrationService;
    private readonly ISnapshotService _snapshotService = snapshotService;
    private readonly IDeviceRepository _deviceRepo = deviceRepo;
    private readonly SecurityAuditService _securityAuditor = securityAuditor;
    private readonly IAuditService _audit = audit;

    [HttpGet("audit")]
    public IActionResult GetAuditSummary()
    {
        var devices = _deviceRepo.GetAll();
        var redCount = 0;
        var yellowCount = 0;
        var greenCount = 0;

        foreach (var d in devices)
        {
            var audit = _securityAuditor.AuditDevice(d);
            if (audit.Grade == SecurityGrade.Red) redCount++;
            else if (audit.Grade == SecurityGrade.Yellow) yellowCount++;
            else greenCount++;
        }

        return Ok(new
        {
            totalDevices = devices.Count,
            criticalRisks = redCount,
            warnings = yellowCount,
            healthyDevices = greenCount,
            timestamp = DateTime.UtcNow
        });
    }

    [HttpPost("rotate-key")]
    public IActionResult RotateKey([FromBody] RotateKeyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword)) return BadRequest("New password is required.");
        
        try 
        {
            _migrationService.RotateKey(request.NewPassword);
            _audit.Record(AuditActions.SecurityKeyRotate, "security", "encryption_key", AuditOutcomes.Success);
            return Ok(new { message = "Database encryption key rotated successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Key rotation failed: {ex.Message}");
        }
    }

    [HttpPost("export-portable")]
    public IActionResult ExportPortable([FromBody] ExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MasterPassword)) return BadRequest("Master password is required for portable export.");

        var tempPath = Path.Combine(Path.GetTempPath(), $"StacksAtlas_Migration_{Guid.NewGuid():N}.db");
        
        try 
        {
            _snapshotService.ExportPortable(request.MasterPassword, tempPath);
            var bytes = System.IO.File.ReadAllBytes(tempPath);
            System.IO.File.Delete(tempPath);
            _audit.Record(AuditActions.SecurityPortableExport, "security", "portable_db", AuditOutcomes.Success);
            
            return File(bytes, "application/octet-stream", "StacksAtlas_Portable_Migration.db");
        }
        catch (Exception ex)
        {
            if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
            return StatusCode(500, $"Export failed: {ex.Message}");
        }
    }

    [HttpPost("import-portable")]
    public async Task<IActionResult> ImportPortable([FromForm] IFormFile file, [FromForm] string masterPassword)
    {
        if (file == null || file.Length == 0) return BadRequest("No file uploaded.");
        if (string.IsNullOrWhiteSpace(masterPassword)) return BadRequest("Master password is required.");

        var tempPath = Path.Combine(Path.GetTempPath(), $"StacksAtlas_Import_{Guid.NewGuid():N}.db");
        
        try 
        {
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            _snapshotService.StageImportPortable(tempPath, masterPassword);
            System.IO.File.Delete(tempPath);
            _audit.Record(AuditActions.SecurityPortableImport, "security", "portable_db", AuditOutcomes.Success);

            return Ok(new { message = "Portable database staged for restore. Please restart the appliance to apply." });
        }
        catch (Exception ex)
        {
            if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
            return StatusCode(500, $"Import failed: {ex.Message}");
        }
    }
}

public record RotateKeyRequest(string NewPassword);
public record ExportRequest(string MasterPassword);
