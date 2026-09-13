namespace StacksAtlas.Portable;

internal static class PortableLog
{
    public static void Write(string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
            File.AppendAllText(PortablePaths.LaunchLog, line + Environment.NewLine);
        }
        catch
        {
            // Best-effort logging only  -  never recreate %LOCALAPPDATA%\StacksAtlas during Close & remove.
        }
    }
}
