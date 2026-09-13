namespace StacksAtlas.Core.Models;

public enum IdentitySource
{
    /// <summary>
    /// Unknown or unclassified.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Lowest confidence: MAC OUI vendor name or generic hostname.
    /// </summary>
    Discovery = 1,

    /// <summary>
    /// Medium confidence: Hostname patterns or NetBIOS probes.
    /// </summary>
    Heuristic = 2,

    /// <summary>
    /// High confidence: Port fingerprints and service banners.
    /// </summary>
    Intelligence = 3,

    /// <summary>
    /// Authoritative: Recog pattern match (HTTP Title, SNMP SysDescr).
    /// </summary>
    Recog = 4,

    /// <summary>
    /// Absolute: Explicitly set by the user.
    /// </summary>
    Manual = 5
}
