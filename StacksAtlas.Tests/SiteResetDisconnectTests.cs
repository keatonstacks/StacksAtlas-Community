using StacksAtlas.Core.Services.Governance;

namespace StacksAtlas.Tests;

public class SiteResetDisconnectTests
{
    [Theory]
    [InlineData(typeof(IOException), "Connection 'abc' disconnected.", true)]
    [InlineData(typeof(InvalidOperationException), "Connection disconnected.", false)]
    [InlineData(typeof(IOException), "Timeout waiting for response.", false)]
    public void IsHubSignalRDisconnectDuringReset_DetectsExpectedDisconnect(Type exceptionType, string message, bool expected)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, message)!;
        Assert.Equal(expected, SiteResetGovernance.IsHubSignalRDisconnectDuringReset(ex));
    }
}
