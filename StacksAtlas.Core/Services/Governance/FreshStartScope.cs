namespace StacksAtlas.Core.Services.Governance;

/// <summary>
/// §7.10 Phase C  -  operator-selected Fresh Start scope.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum FreshStartScope
{
    /// <summary>Wipe StacksAtlas.db (LiteDB) only; preserve Hub fleet inventory.</summary>
    ApplianceOnly = 0,

    /// <summary>Wipe StacksAtlas.Hub.db only; preserve appliance DB and settings.</summary>
    FleetInventoryOnly = 1,

    /// <summary>Wipe both appliance and fleet stores (recommended Hub re-onboarding).</summary>
    ApplianceAndFleet = 2,

    /// <summary>Wipe both stores and reset JSON settings; preserve license.key and db.key.</summary>
    FactoryReset = 3
}

public enum SnapshotStoreKind
{
    Appliance = 0,
    Fleet = 1
}
