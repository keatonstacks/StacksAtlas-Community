using System.Text.Json;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Security;

/// <summary>
/// Resolves the operator-facing dashboard URL. HTTP on 127.0.0.1 is the default;
/// HTTPS on localhost is opt-in after the local CA is trusted.
/// </summary>
public static class ApplianceDashboardUrls
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool WantsHttps(SystemSettings settings) =>
        string.Equals(settings.DashboardScheme, "https", StringComparison.OrdinalIgnoreCase);

    public static bool CanUseHttps(string caPfxPath) =>
        OperatingSystem.IsWindows()
        && File.Exists(caPfxPath)
        && ApplianceTlsTrust.IsTrustedInCurrentUserStore(caPfxPath);

    public static string Resolve(SystemSettings settings, string baseDataDir, bool forceOnboarding = false)
    {
        var path = forceOnboarding || !settings.Onboarding.IsFoundationComplete ? "/onboarding" : "/";
        var caPath = ApplianceTlsTrust.GetCaPfxPath(baseDataDir);

        if (WantsHttps(settings) && CanUseHttps(caPath))
            return $"https://localhost:{settings.HttpsPort}{path}";

        return $"http://127.0.0.1:{settings.HttpPort}{path}";
    }

    public static SystemSettings LoadSettingsOrDefault(string baseDataDir)
    {
        var path = Path.Combine(baseDataDir, "systemsettings.json");
        if (!File.Exists(path))
            return new SystemSettings();

        try
        {
            return JsonSerializer.Deserialize<SystemSettings>(File.ReadAllText(path), JsonOptions)
                ?? new SystemSettings();
        }
        catch
        {
            return new SystemSettings();
        }
    }
}
