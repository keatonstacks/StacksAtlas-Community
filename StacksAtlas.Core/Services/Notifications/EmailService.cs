using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Notifications;

/// <summary>
/// Industrial-grade notification service for SMTP-based alerting.
/// Implements diagnostic logging and temporal consistency.
/// </summary>
public class EmailService(ILogger<EmailService> logger, IClock clock)
{
    public async Task<bool> SendEmailAsync(
        string smtpHost,
        int smtpPort,
        bool useSsl,
        string username,
        string password,
        string fromAddress,
        string fromName,
        string toAddress,
        string subject,
        string htmlBody)
    {
        try
        {
            logger.LogInformation("SMTP: Preparing message for {To} (Host: {Host}:{Port})", toAddress, smtpHost, smtpPort);

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(MailboxAddress.Parse(toAddress));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            
            // Diagnostics: Tracking connection phase
            logger.LogDebug("SMTP: Connecting to {Host}:{Port} (SSL: {Ssl})", smtpHost, smtpPort, useSsl);
            await client.ConnectAsync(smtpHost, smtpPort, useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None);
            
            // Diagnostics: Tracking authentication phase
            logger.LogDebug("SMTP: Authenticating as {Username}", username);
            await client.AuthenticateAsync(username, password);
            
            // Diagnostics: Tracking transmission phase
            logger.LogDebug("SMTP: Sending message...");
            await client.SendAsync(message);
            
            await client.DisconnectAsync(true);

            logger.LogInformation("SMTP: Email sent successfully to {To}", toAddress);
            return true;
        }
        catch (SmtpCommandException ex)
        {
            logger.LogError("SMTP Protocol Error: {Message} (Code: {Code})", ex.Message, ex.StatusCode);
            return false;
        }
        catch (AuthenticationException)
        {
            logger.LogError("SMTP Auth Error: Failed to authenticate as {Username}. Check credentials.", username);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SMTP Critical Error: Failed to send email to {To}", toAddress);
            return false;
        }
    }

    public string GenerateAlertEmailHtml(string deviceName, string deviceIp, string alertType, string message)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f5f5f5; margin: 0; padding: 20px; }}
        .container {{ max-width: 600px; margin: 0 auto; background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }}
        .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; text-align: center; }}
        .header h1 {{ margin: 0; font-size: 24px; font-weight: 900; }}
        .content {{ padding: 30px; }}
        .alert-box {{ background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 15px; margin: 20px 0; border-radius: 4px; }}
        .alert-box.critical {{ background-color: #f8d7da; border-left-color: #dc3545; }}
        .alert-box.success {{ background-color: #d4edda; border-left-color: #28a745; }}
        .device-info {{ background-color: #f8f9fa; padding: 15px; border-radius: 4px; margin: 20px 0; }}
        .device-info strong {{ color: #495057; }}
        .footer {{ background-color: #f8f9fa; padding: 20px; text-align: center; color: #6c757d; font-size: 12px; }}
        .timestamp {{ color: #6c757d; font-size: 14px; margin-top: 20px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🦁 StacksAtlas ALERT</h1>
        </div>
        <div class='content'>
            <div class='alert-box {(alertType.Contains("Down") || alertType.Contains("Offline") ? "critical" : alertType.Contains("Up") ? "success" : "")}'>
                <strong>{alertType}</strong>
            </div>
            <p>{message}</p>
            <div class='device-info'>
                <strong>Device:</strong> {deviceName}<br>
                <strong>IP Address:</strong> {deviceIp}
            </div>
            <div class='timestamp'>
                <strong>Timestamp:</strong> {clock.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
            </div>
        </div>
        <div class='footer'>
            This is an automated alert from StacksAtlas Network Monitoring.<br>
            Do not reply to this email.
        </div>
    </div>
</body>
</html>";
    }

    public async Task<bool> SendAlertEmailAsync(string toAddress, string alertType, Device device, EmailSettings smtp)
    {
        var displayType = alertType switch
        {
            "DeviceDown" => "Device Offline",
            "DeviceUp" => "Device Online",
            "NewDeviceDiscovered" => "New Device Discovered",
            _ => alertType
        };

        var message = alertType switch
        {
            "DeviceDown" => $"Device {device.Name ?? device.IpAddress} has gone offline and is no longer responding to ping requests.",
            "DeviceUp" => $"Device {device.Name ?? device.IpAddress} is back online and responding normally.",
            "NewDeviceDiscovered" => $"A new device {device.Name ?? device.IpAddress} has been discovered on your network.",
            _ => $"Alert: {alertType}"
        };

        var htmlBody = GenerateAlertEmailHtml(
            device.Name ?? device.IpAddress,
            device.IpAddress,
            displayType,
            message
        );

        return await SendEmailAsync(
            smtp.SmtpHost,
            smtp.SmtpPort,
            smtp.UseSsl,
            smtp.Username,
            smtp.Password,
            smtp.FromAddress,
            smtp.FromName,
            toAddress,
            $"StacksAtlas Alert: {displayType}",
            htmlBody
        );
    }
}
