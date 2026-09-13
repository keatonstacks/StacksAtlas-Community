using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Notifications;
using StacksAtlas.Core.Services.Audit;

namespace StacksAtlas.API.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/settings/[controller]")]
public class EmailController : ControllerBase
{
    private readonly EmailSettingsRepository _repo;
    private readonly EmailService _emailService;
    private readonly IAuditService _audit;
    private readonly ILogger<EmailController> _logger;

    public EmailController(
        EmailSettingsRepository repo,
        EmailService emailService,
        IAuditService audit,
        ILogger<EmailController> logger)
    {
        _repo = repo;
        _emailService = emailService;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetSettings()
    {
        var settings = _repo.GetSettings() ?? new EmailSettings();
        
        // Mask password for security
        var masked = new
        {
            settings.SmtpHost,
            settings.SmtpPort,
            settings.UseSsl,
            settings.Username,
            Password = string.IsNullOrEmpty(settings.Password) ? "" : "********",
            settings.FromAddress,
            settings.FromName,
            settings.Enabled
        };

        return Ok(masked);
    }

    [HttpPut]
    public IActionResult UpdateSettings([FromBody] EmailSettings settings)
    {
        try
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(settings.SmtpHost))
                return BadRequest(new { message = "SMTP host is required" });

            if (settings.SmtpPort <= 0 || settings.SmtpPort > 65535)
                return BadRequest(new { message = "Invalid SMTP port" });

            if (string.IsNullOrWhiteSpace(settings.Username))
                return BadRequest(new { message = "Username is required" });

            // If password is masked, keep existing password
            var existing = _repo.GetSettings();
            if (settings.Password == "********" && existing != null)
            {
                settings.Password = existing.Password;
            }

            var userName = User.Identity?.Name ?? "Anonymous/System";
            _repo.SaveSettings(settings);
            _logger.LogInformation("Email settings updated by {User}", userName);
            _audit.Record(
                AuditActions.SettingsEmailUpdate,
                "settings",
                "email",
                AuditOutcomes.Success,
                detail: $"host={settings.SmtpHost}; enabled={settings.Enabled}");

            return Ok(new { message = "Email settings saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update email settings");
            return StatusCode(500, new { message = "Failed to save settings" });
        }
    }

    [HttpPost("test")]
    public async Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest request)
    {
        try
        {
            var settings = _repo.GetSettings();
            if (settings == null || !settings.Enabled)
                return BadRequest(new { message = "Email is not configured or enabled" });

            if (string.IsNullOrWhiteSpace(request.ToAddress))
                return BadRequest(new { message = "Recipient email address is required" });

            var htmlBody = _emailService.GenerateAlertEmailHtml(
                "Test Device",
                "192.168.1.100",
                "Test Alert",
                "This is a test email from StacksAtlas. If you received this, your email configuration is working correctly!"
            );

            var success = await _emailService.SendEmailAsync(
                settings.SmtpHost,
                settings.SmtpPort,
                settings.UseSsl,
                settings.Username,
                settings.Password,
                settings.FromAddress,
                settings.FromName,
                request.ToAddress,
                "StacksAtlas Test Alert",
                htmlBody
            );

            if (success)
            {
                _logger.LogInformation("Test email sent successfully to {To}", request.ToAddress);
                return Ok(new { message = $"Test email sent successfully to {request.ToAddress}" });
            }
            else
            {
                return StatusCode(500, new { message = "Failed to send test email. Check server logs for details." });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send test email");
            return StatusCode(500, new { message = $"Error: {ex.Message}" });
        }
    }
}

public class TestEmailRequest
{
    public string ToAddress { get; set; } = "";
}
