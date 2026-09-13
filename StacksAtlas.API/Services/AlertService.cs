using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Services.Notifications;
using StacksAtlas.Core.Settings;
using System.Collections.Concurrent;

namespace StacksAtlas.API.Services;

public interface IAlertService
{
    Task EvaluateAndSendAlertsAsync(List<Device> currentDevices, Dictionary<Guid, string> preSweepStatuses);
    Task DelegateAlertAsync(AlertEventType alertType, Device device, string nodeId);
}

public class AlertService(
    ILogger<AlertService> logger,
    EmailSettingsRepository emailSettingsRepo,
    EmailService emailService,
    IAlertEventRepository alertEventRepo,
    WebhookRepository webhookRepo,
    WebhookService webhookService,
    IAuthService authService,
    FederationSettingsStore federationSettingsStore,
    IClock clock) : IAlertService
{
    private readonly ILogger<AlertService> _logger = logger;
    private readonly EmailSettingsRepository _emailSettingsRepo = emailSettingsRepo;
    private readonly EmailService _emailService = emailService;
    private readonly IAlertEventRepository _alertEventRepo = alertEventRepo;
    private readonly WebhookRepository _webhookRepo = webhookRepo;
    private readonly WebhookService _webhookService = webhookService;
    private readonly IAuthService _authService = authService;
    private readonly FederationSettingsStore _federationSettings = federationSettingsStore;
    private readonly IClock _clock = clock;

    private readonly ConcurrentDictionary<string, DateTime> _alertCooldowns = new();
    private bool _isInitialized = false;

    public async Task EvaluateAndSendAlertsAsync(List<Device> currentDevices, Dictionary<Guid, string> preSweepStatuses)
    {
        try
        {
            // Prevent flood on first run
            if (!_isInitialized)
            {
                _isInitialized = true;
                _logger.LogInformation("Alerting: Baseline established for {Count} devices.", currentDevices.Count);
                return;
            }

            var emailSettings = _emailSettingsRepo.GetSettings();
            var usersWithAlerts = _authService.GetAllUsers()
                .Where(u => u.AlertsEnabled)
                .ToList();

            foreach (var device in currentDevices)
            {
                if (preSweepStatuses.TryGetValue(device.Id, out var previousStatus))
                {
                    if (previousStatus == "online" && device.Status == "offline")
                        await SendAlertAsync(AlertEventType.DeviceDown, device, usersWithAlerts, emailSettings);
                    else if (previousStatus == "offline" && device.Status == "online")
                        await SendAlertAsync(AlertEventType.DeviceUp, device, usersWithAlerts, emailSettings);
                }
                else
                {
                    await SendAlertAsync(AlertEventType.NewDeviceDiscovered, device, usersWithAlerts, emailSettings);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate alerts");
        }
    }

    public async Task DelegateAlertAsync(AlertEventType alertType, Device device, string nodeId)
    {
        try
        {
            _logger.LogInformation("Alerting: Hub executing delegated alert for device {DeviceIp} ({AlertType}) on behalf of Node {NodeId}", 
                device.IpAddress, alertType, nodeId);

            var emailSettings = _emailSettingsRepo.GetSettings();
            var usersWithAlerts = _authService.GetAllUsers()
                .Where(u => u.AlertsEnabled)
                .ToList();

            // Set provenance details before sending
            device.NodeId = nodeId;

            await SendAlertAsync(alertType, device, usersWithAlerts, emailSettings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute delegated alert for Node {NodeId}", nodeId);
        }
    }

    private async Task SendAlertAsync(AlertEventType alertType, Device device, List<User> users, EmailSettings? smtp)
    {
        var cooldownKey = $"{device.Id}_{alertType}";
        if (_alertCooldowns.TryGetValue(cooldownKey, out var lastAlert) && _clock.UtcNow - lastAlert < TimeSpan.FromMinutes(5))
            return;

        if (StacksAtlas.Core.State.ExecutionState.IsFederationActive && _federationSettings.Current.DelegateAlertDispatch)
        {
            var delegatedEmails = new List<string>();
            var delegatedWebhooks = new List<string>();

            _alertEventRepo.LogAlert(new AlertEvent
            {
                Id = Guid.NewGuid().ToString(),
                TriggeredAt = _clock.UtcNow,
                DeviceId = device.Id.ToString(),
                DeviceName = device.Name ?? "Unknown",
                DeviceIp = device.IpAddress,
                AlertType = alertType,
                SentToEmails = delegatedEmails,
                SentToWebhooks = delegatedWebhooks,
                Success = false,
                ErrorMessage = "Delegated to Hub",
                NodeId = _federationSettings.Current.NodeId
            }, device, isDelegation: true);

            _alertCooldowns[cooldownKey] = _clock.UtcNow;
            _logger.LogInformation("Alerting: Node delegated alert {Type} for {Device} to Hub.", alertType, device.Name ?? device.IpAddress);
            return;
        }

        // --- Dispatch to Webhooks ---
        var activeWebhooks = _webhookRepo.GetActive();
        var eligibleWebhooks = activeWebhooks.Where(w => ShouldTriggerWebhook(w, alertType, device)).ToList();
        
        if (eligibleWebhooks.Any())
        {
            await _webhookService.ProcessAlertAsync(alertType, device, eligibleWebhooks);
        }

        // --- Dispatch to Emails & Personal Webhooks ---
        var recipients = users.Where(u => ShouldReceiveAlert(u, alertType, device)).ToList();
        
        var sentEmails = new List<string>();
        var sentWebhooks = new List<string>();
        var success = true;
        string? errorMessage = null;

        // B1. Track Global/Targeted Webhooks sent by Service
        if (eligibleWebhooks.Any())
        {
            sentWebhooks.AddRange(eligibleWebhooks.Select(w => w.Name));
        }

        foreach (var user in recipients)
        {
            try
            {
                // A. Dispatch to Email
                var email = user.AlertEmail ?? user.Email;
                if (!string.IsNullOrEmpty(email) && smtp != null && smtp.Enabled)
                {
                    await _emailService.SendAlertEmailAsync(email, alertType.ToString(), device, smtp);
                    sentEmails.Add(email);
                }

                // B. Dispatch to User's Preferred Webhook(s)
                if (user.WebhookEnabled && user.PreferredWebhookIds.Any())
                {
                    var userWebhooks = activeWebhooks.Where(w => user.PreferredWebhookIds.Contains(w.Id)).ToList();
                    if (userWebhooks.Any())
                    {
                        await _webhookService.ProcessAlertAsync(alertType, device, userWebhooks);
                        foreach(var userWebhook in userWebhooks)
                        {
                            sentWebhooks.Add($"{user.Username}'s {userWebhook.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorMessage = ex.Message;
                _logger.LogWarning(ex, "Failed to send alert notification to {User}", user.Username);
            }
        }

        if (sentEmails.Count > 0 || sentWebhooks.Count > 0 || !success)
        {
            _alertEventRepo.LogAlert(new AlertEvent
            {
                Id = Guid.NewGuid().ToString(),
                TriggeredAt = _clock.UtcNow,
                DeviceId = device.Id.ToString(),
                DeviceName = device.Name ?? "Unknown",
                DeviceIp = device.IpAddress,
                AlertType = alertType,
                SentToEmails = sentEmails,
                SentToWebhooks = sentWebhooks,
                Success = success,
                ErrorMessage = errorMessage,
                NodeId = device.NodeId
            });

            _alertCooldowns[cooldownKey] = _clock.UtcNow;
            _logger.LogInformation("Alert sent: {Type} for {Device} to {EmailCount} emails and {WebhookCount} webhooks", 
                alertType, device.Name ?? device.IpAddress, sentEmails.Count, sentWebhooks.Count);
        }
    }

    private bool ShouldTriggerWebhook(WebhookConfiguration webhook, AlertEventType alertType, Device device)
    {
        if (!device.AlertsEnabled) return false;
        
        // Check event type filtering
        if (!webhook.TriggerEvents.Contains(alertType)) return false;

        // Check scoping rules (Matching User Profile consistency)
        if (webhook.AlertSeverity == "AssignedOnly")
        {
            if (webhook.AssignedToUserId == null) return false;
            return device.ManagedByUserId == webhook.AssignedToUserId;
        }

        return true;
    }

    private bool ShouldReceiveAlert(User user, AlertEventType alertType, Device device)
    {
        if (!device.AlertsEnabled) return false;
        
        if (alertType == AlertEventType.DeviceDown && !user.AlertOnDeviceDown) return false;
        if (alertType == AlertEventType.DeviceUp && !user.AlertOnDeviceUp) return false;
        if (alertType == AlertEventType.NewDeviceDiscovered && !user.AlertOnNewDevice) return false;

        if (user.AlertSeverity == "AssignedOnly")
            return device.ManagedByUserId == user.Id;

        return true;
    }
}
