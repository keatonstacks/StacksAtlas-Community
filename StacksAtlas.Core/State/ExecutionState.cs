using StacksAtlas.Core.Models;
using StacksAtlas.Core.Helpers;

namespace StacksAtlas.Core.State;

/// <summary>
/// Global state provider for the current application execution mode.
/// Populated during Program.cs bootstrap.
/// </summary>
public class ExecutionState
{
    private static ExecutionMode _mode = ExecutionMode.Standalone;
    private static string? _hubUrl;
    private static bool _tailnetFederation;

    public static ExecutionMode Mode => _mode;
    public static string? HubUrl => _hubUrl;

    public static bool IsHub => _mode == ExecutionMode.Hub;
    public static bool IsStandalone => _mode == ExecutionMode.Standalone;

    public static bool IsScanningEnabled => _mode == ExecutionMode.Standalone;
    public static bool IsFederationActive =>
        _mode == ExecutionMode.Standalone && (!string.IsNullOrEmpty(_hubUrl) || _tailnetFederation);
    public static bool IsHubBrainEnabled => _mode == ExecutionMode.Hub;

    /// <summary>True when STACKSATLAS_PORTABLE=1  -  evaluation without system service.</summary>
    public static bool IsPortable => PortableMode.IsEnabled;

    public static void Initialize(ExecutionMode mode, string? hubUrl = null, bool tailnetFederation = false)
    {
        _mode = mode;
        _hubUrl = hubUrl;
        _tailnetFederation = tailnetFederation;
    }
}
