namespace StacksAtlas.Portable;

internal static class PortablePaths
{
    public static string RootDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StacksAtlas");

    public static string AppDir => Path.Combine(RootDir, "app");

    public static string DataDir => Path.Combine(RootDir, "data");

    public static string BundleDir => Path.Combine(DataDir, ".dotnet-bundle");

    public static string ApiExe => Path.Combine(AppDir, "StacksAtlas.API.exe");

    public static string VersionFile => Path.Combine(AppDir, "version.txt");

    public static string LaunchLog =>
        Path.Combine(Path.GetTempPath(), "StacksAtlas.portable-launch.log");

    public static string DashboardUrl => "http://127.0.0.1:5000/onboarding";

    public static string SecureDashboardUrl => "https://localhost:5001/onboarding";

    public static string CloseUrl => "http://127.0.0.1:5000/api/system/portable/close";
}
