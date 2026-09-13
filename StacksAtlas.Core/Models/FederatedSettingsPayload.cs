using System.Collections.Generic;

namespace StacksAtlas.Core.Models;

/// <summary>
/// Payload transmitted by the Hub to sync alerting (SMTP, Webhooks) and SIEM (Syslog) settings down to Nodes.
/// </summary>
public class FederatedSettingsPayload
{
    // Alerting Settings
    public EmailSettings? EmailSettings { get; set; }
    public List<WebhookConfiguration>? Webhooks { get; set; }

    // SIEM Settings
    public bool? SyslogEnabled { get; set; }
    public string? SyslogHost { get; set; }
    public int? SyslogPort { get; set; }
    public string? SyslogAppName { get; set; }
}
