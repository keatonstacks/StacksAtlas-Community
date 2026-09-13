using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Integrations;

namespace StacksAtlas.Tests;

/// <summary>
/// Tests OpenAVC reading extraction helpers exposed for verification.
/// </summary>
public class OpenAvcReadingExtractionTests
{
    [Fact]
    public void ExtractReadingValue_PrefersLongestStateMessage()
    {
        var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["last_raw_response"] = "+OK",
            ["last_message"] = "Installed Apps (32):\n  Netflix netflix\n  YouTube youtube.leanback.v4",
        };

        var value = OpenAvcReadingTestHooks.ExtractReadingValue(state, null, null);

        Assert.Contains("Installed Apps (32)", value);
        Assert.Contains("Netflix", value);
    }

    [Fact]
    public void ExtractReadingValue_UsesHttpSummaryWhenStateEmpty()
    {
        var value = OpenAvcReadingTestHooks.ExtractReadingValue(
            null,
            null,
            "Installed Apps (32):\n  Netflix netflix");

        Assert.Contains("Netflix", value);
    }

    [Fact]
    public void InferReadingLabel_AppsMacro_ReturnsInstalledApps()
    {
        Assert.Equal("Installed apps", OpenAvcReadingTestHooks.InferReadingLabel("lg_list_apps", "LG List Apps"));
    }
}

/// <summary>Test-only surface for private OpenAVC reading helpers.</summary>
internal static class OpenAvcReadingTestHooks
{
    public static string? ExtractReadingValue(
        Dictionary<string, string>? state,
        object? httpResult,
        string? httpSummary) =>
        typeof(OpenAvcService)
            .GetMethod(
                "ExtractReadingValue",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [state, httpResult, httpSummary]) as string;

    public static string InferReadingLabel(string actionId, string? actionName) =>
        (string)typeof(OpenAvcService)
            .GetMethod(
                "InferReadingLabel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [actionId, actionName])!;
}
