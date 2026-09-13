using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.State;

/// <summary>
/// Prevents accidental Hub downgrade when the relational Hub brain database already exists.
/// </summary>
public static class HubModeGuard
{
    public static string HubDatabasePath =>
        Path.Combine(PlatformPaths.BaseDataDir, "StacksAtlas.Hub.db");

    public static bool IsHubBrainPresent() =>
        File.Exists(HubDatabasePath);

    public static ExecutionMode ResolveStartupMode(ExecutionMode configuredMode)
    {
        if (ExecutionState.IsPortable)
        {
            if (configuredMode == ExecutionMode.Hub)
            {
                Console.WriteLine("StacksAtlas: Portable mode  -  Hub execution is disabled. Running Standalone.");
            }
            else if (IsHubBrainPresent())
            {
                Console.WriteLine(
                    "StacksAtlas: Portable mode  -  ignoring Hub database at {0}. Running Standalone.",
                    HubDatabasePath);
            }

            return ExecutionMode.Standalone;
        }

        if (IsHubBrainPresent() && configuredMode != ExecutionMode.Hub)
        {
            Console.WriteLine(
                "StacksAtlas: Hub database detected at {0}  -  enforcing Hub execution mode.",
                HubDatabasePath);
            return ExecutionMode.Hub;
        }

        return configuredMode;
    }

    public static void EnforceHubMode(FederationSettings settings)
    {
        if (ExecutionState.IsPortable)
        {
            settings.Mode = ExecutionMode.Standalone;
            return;
        }

        if (IsHubBrainPresent() || ExecutionState.IsHub)
            settings.Mode = ExecutionMode.Hub;
    }
}
