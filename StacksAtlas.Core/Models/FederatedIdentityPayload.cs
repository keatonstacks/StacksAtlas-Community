using System.Collections.Generic;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Models;

/// <summary>
/// Payload transmitted by the Hub to sync all active users and authentication settings down to Nodes.
/// Provides offline SSO and password-auth resiliency for remote appliances.
/// </summary>
public class FederatedIdentityPayload
{
    public List<User>? Users { get; set; }
    public AuthSettings? AuthSettings { get; set; }
}
