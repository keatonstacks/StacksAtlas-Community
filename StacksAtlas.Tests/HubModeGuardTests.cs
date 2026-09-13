using StacksAtlas.Core.Models;
using StacksAtlas.Core.State;
using Xunit;

namespace StacksAtlas.Tests;

public class HubModeGuardTests : IDisposable
{
    public void Dispose()
    {
        ExecutionState.Initialize(ExecutionMode.Standalone);
    }

    [Fact]
    public void EnforceHubMode_ForcesHub_WhenAlreadyRunningAsHub()
    {
        try
        {
            ExecutionState.Initialize(ExecutionMode.Hub, null, false);
            var settings = new FederationSettings { Mode = ExecutionMode.Standalone };

            HubModeGuard.EnforceHubMode(settings);

            Assert.Equal(ExecutionMode.Hub, settings.Mode);
        }
        finally
        {
            ExecutionState.Initialize(ExecutionMode.Standalone);
        }
    }
}
