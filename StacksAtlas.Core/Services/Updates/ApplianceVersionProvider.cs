using System.Reflection;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Models.Updates;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;
using Microsoft.Extensions.Options;

namespace StacksAtlas.Core.Services.Updates;

public sealed class ApplianceVersionProvider(
    IClock clock,
    IOptions<UpdateSettings> updateSettings) : IApplianceVersionProvider
{
    private readonly UpdateSettings _updateSettings = updateSettings.Value;
    private string? _cachedVersion;

    public ApplianceVersionInfo GetCurrent()
    {
        var version = ResolveVersionString();
        var isPortable = PortableMode.IsEnabled;
        return new ApplianceVersionInfo(
            Version: version,
            Channel: _updateSettings.DefaultChannel,
            InstallMode: PortableMode.InstallModeLabel,
            IsPortable: isPortable,
            ArtifactKey: UpdateArtifactKeys.ResolveForRuntime(isPortable),
            ReportedUtc: clock.UtcNow);
    }

    private string ResolveVersionString()
    {
        if (_cachedVersion != null)
            return _cachedVersion;

        var assemblyVersion = typeof(ApplianceVersionProvider).Assembly.GetName().Version;
        if (assemblyVersion != null)
        {
            _cachedVersion = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
            return _cachedVersion;
        }

        var informational = typeof(ApplianceVersionProvider).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        _cachedVersion = string.IsNullOrWhiteSpace(informational)
            ? "0.0.0"
            : informational.Split('+')[0].Trim();
        return _cachedVersion;
    }
}
