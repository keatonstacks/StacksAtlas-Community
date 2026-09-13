namespace StacksAtlas.Core.Models;

public enum ExecutionMode
{
    /// <summary>
    /// Default mode. Standalone appliance behavior (LiteDB). 
    /// If HubUrl is provided, it also functions as a federated Node.
    /// </summary>
    Standalone,

    /// <summary>
    /// The central brain (SQLite). Hosts the global dashboard and fleet reconciliation.
    /// </summary>
    Hub
}
