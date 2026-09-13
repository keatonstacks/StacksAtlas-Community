using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Services.Security;

namespace StacksAtlas.API.Services;

public class WebhookService(
    IHttpClientFactory httpClientFactory,
    WebhookRepository repository,
    SettingsEncryptor encryptor,
    IClock clock,
    ILogger<WebhookService> logger)
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Webhooks");
    private readonly WebhookRepository _repository = repository;
    private readonly SettingsEncryptor _encryptor = encryptor;
    private readonly IClock _clock = clock;
    private readonly ILogger<WebhookService> _logger = logger;

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task ProcessAlertAsync(AlertEventType type, Device device, List<WebhookConfiguration> webhooks)
    {
        foreach (var webhook in webhooks)
        {
            if (!webhook.Enabled || webhook.Status == WebhookStatus.Disabled) continue;
            
            // Circuit Breaker Check
            if (webhook.Status == WebhookStatus.CircuitOpen)
            {
                if (webhook.CircuitResetAt.HasValue && _clock.UtcNow > webhook.CircuitResetAt.Value)
                {
                    _logger.LogInformation("Webhook Circuit Resetting for {Name}.", webhook.Name);
                    _repository.UpdateStatus(webhook.Id, WebhookStatus.Active);
                }
                else
                {
                    continue; // Skip while circuit is open
                }
            }

            if (webhook.TriggerEvents.Contains(type))
            {
                // Fire and forget (with internal retry loop) to not block the discovery engine
                _ = Task.Run(() => SendWithRetryAsync(webhook, type, device));
            }
        }
    }

    private async Task SendWithRetryAsync(WebhookConfiguration webhook, AlertEventType type, Device device)
    {
        int maxRetries = 3;
        int attempt = 0;

        while (attempt <= maxRetries)
        {
            try
            {
                var success = await ExecuteSendAsync(webhook, type, device);
                if (success)
                {
                    if (webhook.FailureCount > 0)
                        _repository.UpdateStatus(webhook.Id, WebhookStatus.Active);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Webhook attempt {Attempt} failed for {Name}", attempt + 1, webhook.Name);
            }

            attempt++;
            if (attempt <= maxRetries)
            {
                // Exponential Backoff with Jitter: 1min -> 5min -> 15min
                var baseDelay = attempt switch
                {
                    1 => TimeSpan.FromMinutes(1),
                    2 => TimeSpan.FromMinutes(5),
                    _ => TimeSpan.FromMinutes(15)
                };

                // Add Jitter (±10%)
                var jitter = baseDelay.TotalSeconds * 0.1 * (Random.Shared.NextDouble() * 2 - 1);
                var delay = TimeSpan.FromSeconds(baseDelay.TotalSeconds + jitter);

                await Task.Delay(delay);
            }
        }

        // If we get here, all retries failed
        _repository.IncrementFailure(webhook.Id, "Max retries exceeded or remote service rejected request.");
    }

    private async Task<bool> ExecuteSendAsync(WebhookConfiguration webhook, AlertEventType type, Device device)
    {
        var url = _encryptor.Unprotect(webhook.Url);
        if (string.IsNullOrEmpty(url)) return false;

        _logger.LogDebug("Webhook Dispatch: {Name} | Provider: {Provider} | Type: {Type}", webhook.Name, webhook.Provider, type);
        var payload = FormatPayload(webhook.Provider, type, device);
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        
        _logger.LogDebug("Outgoing Webhook Payload ({Name}): {Payload}", webhook.Name, json);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        // Optional HMAC Signature
        if (!string.IsNullOrEmpty(webhook.SigningSecret))
        {
            var signature = GenerateSignature(json, webhook.SigningSecret);
            request.Headers.Add("X-StacksAtlas-Signature", signature);
        }

        request.Headers.UserAgent.ParseAdd("StacksAtlas-Appliance/1.2.7");

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Webhook destination {Name} returned {Status}: {Error}", webhook.Name, response.StatusCode, error);
            return false;
        }

        return true;
    }

    private object FormatPayload(WebhookProvider provider, AlertEventType type, Device device)
    {
        // Explicitly cast to int to be bulletproof against Enum mapping oddities
        int providerId = (int)provider;
        _logger.LogDebug("Webhook Format Selection: ProviderEnum={Provider}, ProviderInt={ProviderId}", provider, providerId);

        if (providerId == 3 || provider == WebhookProvider.Discord)
            return FormatDiscordPayload(type, device);
        
        if (providerId == 1 || provider == WebhookProvider.Slack)
            return FormatSlackPayload(type, device);
            
        if (providerId == 2 || provider == WebhookProvider.Teams)
            return FormatTeamsPayload(type, device);

        return FormatGenericPayload(type, device);
    }

    private object FormatDiscordPayload(AlertEventType type, Device device)
    {
        var color = type == AlertEventType.DeviceDown ? 15158332 : 3066993; // Red vs Green
        var title = type switch
        {
            AlertEventType.DeviceDown => "🔴 Device Offline",
            AlertEventType.DeviceUp => "🟢 Device Online",
            _ => "ℹ️ New Device Discovered"
        };

        var embed = new Dictionary<string, object>
        {
            { "title", title },
            { "description", $"**{device.Name ?? device.Hostname ?? "Unknown Device"}** changed status to **{device.Status}**." },
            { "color", color },
            { "fields", new[]
                {
                    new { name = "IP Address", value = device.IpAddress ?? "Unknown", inline = true },
                    new { name = "MAC Address", value = device.MacAddress ?? "Unknown", inline = true },
                    new { name = "Vendor", value = device.Vendor ?? "Unknown", inline = true }
                }
            },
            { "footer", new { text = $"StacksAtlas Appliance • {_clock.UtcNow:HH:mm:ss UTC}" } },
            { "timestamp", _clock.UtcNow.ToString("o") }
        };

        return new Dictionary<string, object>
        {
            { "content", $"**StacksAtlas Alert:** {title}" },
            { "username", "StacksAtlas" },
            { "embeds", new[] { embed } }
        };
    }

    private object FormatSlackPayload(AlertEventType type, Device device)
    {
        var color = type == AlertEventType.DeviceDown ? "#E01E5A" : "#2EB67D"; // Slack Red vs Slack Green
        var title = type switch
        {
            AlertEventType.DeviceDown => "🔴 Device Offline",
            AlertEventType.DeviceUp => "🟢 Device Online",
            _ => "ℹ️ New Device Discovered"
        };

        return new
        {
            text = $"{title}: {device.Name ?? device.IpAddress}",
            attachments = new[]
            {
                new
                {
                    color = color,
                    blocks = new object[]
                    {
                        new { type = "header", text = new { type = "plain_text", text = title } },
                        new { type = "section", text = new { type = "mrkdwn", text = $"*Device:* {device.Name ?? "Unknown"}\n*Status:* `{device.Status}`" } },
                        new { type = "section", fields = new[]
                        {
                            new { type = "mrkdwn", text = $"*IP Address:*\n{device.IpAddress}" },
                            new { type = "mrkdwn", text = $"*MAC Address:*\n{device.MacAddress ?? "Unknown"}" },
                            new { type = "mrkdwn", text = $"*Vendor:*\n{device.Vendor ?? "Unknown"}" }
                        }},
                        new { type = "context", elements = new[]
                        {
                            new { type = "mrkdwn", text = $"📍 StacksAtlas Appliance • {DateTime.UtcNow:HH:mm:ss UTC}" }
                        }}
                    }
                }
            }
        };
    }

    private object FormatTeamsPayload(AlertEventType type, Device device)
    {
        var color = type == AlertEventType.DeviceDown ? "Attention" : "Good"; // Teams Red vs Green
        var title = type switch
        {
            AlertEventType.DeviceDown => "🔴 Device Offline",
            AlertEventType.DeviceUp => "🟢 Device Online",
            _ => "ℹ️ New Device Discovered"
        };

        // Adaptive Card 1.5+ for Teams Workflows
        return new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.5",
                        body = new object[]
                        {
                            new { type = "TextBlock", text = title, weight = "Bolder", size = "Large", color = color },
                            new { type = "TextBlock", text = $"**{device.Name ?? "Unknown Device"}** has changed status.", spacing = "None" },
                            new { type = "FactSet", spacing = "Medium", facts = new[] {
                                new { title = "Status", value = device.Status ?? "Unknown" },
                                new { title = "IP Address", value = device.IpAddress ?? "Unknown" },
                                new { title = "MAC Address", value = device.MacAddress ?? "Unknown" },
                                new { title = "Vendor", value = device.Vendor ?? "Unknown" }
                            }},
                            new { type = "TextBlock", text = $"StacksAtlas Appliance • {DateTime.UtcNow:HH:mm:ss UTC}", size = "Small", isSubtle = true, spacing = "Large" }
                        }
                    }
                }
            }
        };
    }

    private object FormatGenericPayload(AlertEventType type, Device device)
    {
        return new
        {
            version = "1.0",
            timestamp = _clock.UtcNow,
            alertType = type.ToString(),
            device = new
            {
                device.Id,
                device.IpAddress,
                device.MacAddress,
                device.Name,
                device.Status,
                device.Vendor
            }
        };
    }

    private string GenerateSignature(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(payloadBytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }
}
