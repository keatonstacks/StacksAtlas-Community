using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using StacksAtlas.Core.Services.Reports;
using StacksAtlas.Core.Models;

namespace StacksAtlas.API.Controllers;

[Authorize]
[ApiController]
[Route("api/reports")]
public class ReportsController(ReportService reportService, ILogger<ReportsController> logger) : ControllerBase
{
    private readonly ReportService _reportService = reportService;
    private readonly ILogger<ReportsController> _logger = logger;

    [HttpGet("inventory/csv")]
    public IActionResult DownloadInventoryCsv([FromQuery(Name = "nodeId")] string[]? nodeIds = null, [FromQuery] string? client = null, [FromQuery] string? building = null, [FromQuery] string? room = null)
    {
        var csvBytes = _reportService.GenerateInventoryCsv(nodeIds, client, building, room);
        var fileName = $"StacksAtlas_Inventory_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(csvBytes, "text/csv", fileName);
    }

    [HttpGet("inventory/pdf")]
    public IActionResult DownloadAuditPdf([FromQuery(Name = "nodeId")] string[]? nodeIds = null, [FromQuery] string? client = null, [FromQuery] string? building = null, [FromQuery] string? room = null)
    {
        try
        {
            var pdfBytes = _reportService.GenerateAuditPdf(nodeIds, client, building, room);
            var fileName = $"StacksAtlas_AuditReport_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF API Generation Error");
            var message = ex.InnerException != null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;
            return StatusCode(500, $"PDF Generation Failed: {message}\nEnsure native dependencies (libfontconfig1) are installed in the host environment.");
        }
    }

    [HttpGet("summary")]
    public ActionResult GetSummary([FromQuery(Name = "nodeId")] string[]? nodeIds = null, [FromQuery] string? client = null, [FromQuery] string? building = null, [FromQuery] string? room = null)
    {
        // Use the consolidated service method to ensure PDF and UI see identical data
        var summary = _reportService.GetFullSummary(nodeIds, client, building, room);
        return Ok(summary);
    }
}
