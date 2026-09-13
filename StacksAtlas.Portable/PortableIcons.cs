using System.Reflection;

namespace StacksAtlas.Portable;

internal static class PortableIcons
{
    private const string EmbeddedIconName = "StacksAtlas.Portable.StacksAtlas.ico";

    public static Icon LoadTrayIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(EmbeddedIconName);
            if (stream is not null)
                return new Icon(stream);
        }
        catch
        {
            // Fall through to associated icon.
        }

        try
        {
            var path = Environment.ProcessPath ?? Application.ExecutablePath;
            var associated = Icon.ExtractAssociatedIcon(path);
            if (associated is not null)
                return (Icon)associated.Clone();
        }
        catch
        {
            // Fall through to system default.
        }

        return SystemIcons.Application;
    }
}
