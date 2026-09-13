using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Services.Audit;

namespace StacksAtlas.API.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/[controller]")]
public class WebhooksController(
    WebhookRepository repository,
    WebhookService webhookService,
    SettingsEncryptor encryptor,
    IAuditService audit,
    ILogger<WebhooksController> logger) : ControllerBase
{
    private readonly WebhookRepository _repository = repository;
    private readonly WebhookService _webhookService = webhookService;
    private readonly SettingsEncryptor _encryptor = encryptor;
    private readonly IAuditService _audit = audit;
    private readonly ILogger<WebhooksController> _logger = logger;

    [HttpGet]
    public IActionResult GetAll()
    {
        var webhooks = _repository.GetAll();
        // Mask URLs and secrets for safety
        foreach (var webhook in webhooks)
        {
            webhook.Url = MaskUrl(webhook.Url);
            webhook.SigningSecret = string.IsNullOrEmpty(webhook.SigningSecret) ? null : "********";
        }
        return Ok(webhooks);
    }

    [HttpPost]
    public IActionResult Create([FromBody] WebhookConfiguration config)
    {
        if (string.IsNullOrEmpty(config.Url)) return BadRequest("URL is required.");
        
        config.Id = Guid.NewGuid();
        config.Url = _encryptor.Protect(config.Url);
        
        if (!string.IsNullOrEmpty(config.SigningSecret))
            config.SigningSecret = config.SigningSecret; // Keep plain until we decide to encrypt this too (optional)

        _repository.Upsert(config);
        _audit.Record(AuditActions.WebhookCreate, "webhook", config.Id.ToString(), AuditOutcomes.Success, detail: $"name={config.Name}; provider={config.Provider}");
        return CreatedAtAction(nameof(GetAll), new { id = config.Id }, config);
    }

    [HttpPut("{id}")]
    public IActionResult Update(Guid id, [FromBody] WebhookConfiguration config)
    {
        var existing = _repository.GetById(id);
        if (existing == null) return NotFound();

        existing.Name = config.Name;
        existing.Provider = config.Provider;
        existing.Enabled = config.Enabled;
        existing.TriggerEvents = config.TriggerEvents;
        existing.SigningSecret = config.SigningSecret == "********" ? existing.SigningSecret : config.SigningSecret;

        // Only update URL if it's not the masked one
        if (!config.Url.Contains("********"))
        {
            existing.Url = _encryptor.Protect(config.Url);
        }

        _repository.Upsert(existing);
        _audit.Record(AuditActions.WebhookUpdate, "webhook", id.ToString(), AuditOutcomes.Success, detail: $"name={existing.Name}; enabled={existing.Enabled}");
        return NoContent();
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(Guid id)
    {
        _repository.Delete(id);
        _audit.Record(AuditActions.WebhookDelete, "webhook", id.ToString(), AuditOutcomes.Success);
        return NoContent();
    }

    [HttpPost("{id}/test")]
    public async Task<IActionResult> TestWebhook(Guid id)
    {
        var webhook = _repository.GetById(id);
        if (webhook == null) return NotFound();

        var dummyDevice = new Device
        {
            Name = "TEST-DEVICE",
            IpAddress = "127.0.0.1",
            Status = "online",
            Vendor = "StacksAtlas Simulator"
        };

        await _webhookService.ProcessAlertAsync(AlertEventType.DeviceUp, dummyDevice, new List<WebhookConfiguration> { webhook });
        
        return Ok(new { message = "Test payload dispatched to background queue." });
    }

    private string MaskUrl(string encryptedUrl)
    {
        try
        {
            var url = _encryptor.Unprotect(encryptedUrl);
            if (string.IsNullOrEmpty(url)) return "********";
            
            var uri = new Uri(url);
            // Mask the path/token part, keep the host
            return $"{uri.Scheme}://{uri.Host}/.../********";
        }
        catch
        {
            return "********";
        }
    }
}
