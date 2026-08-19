using System.Drawing;
using System.IO;
using System.Windows;

namespace ShuBingDesktopPet.Services;

public static class TrayIconLoader
{
    public static Icon? ResolveTrayIcon(
        string? executablePath = null,
        Func<string?, Icon?>? associatedIconProvider = null,
        Func<Icon?>? resourceIconProvider = null)
    {
        var path = executablePath ?? Environment.ProcessPath;
        var associated = associatedIconProvider ?? ExtractAssociatedIcon;
        var resource = resourceIconProvider ?? LoadEmbeddedIcon;

        var associatedIcon = SafeLoad(() => associated(path));
        if (associatedIcon is not null)
        {
            return associatedIcon;
        }

        return SafeLoad(resource);
    }

    private static Icon? ExtractAssociatedIcon(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        if (!File.Exists(executablePath))
        {
            return null;
        }

        var icon = Icon.ExtractAssociatedIcon(executablePath);
        if (icon is null)
        {
            return null;
        }

        return icon;
    }

    private static Icon? LoadEmbeddedIcon()
    {
        var resourceUri = new Uri("pack://application:,,,/Assets/ShuBingDesktopPet.ico");
        var streamInfo = global::System.Windows.Application.GetResourceStream(resourceUri);
        if (streamInfo is null)
        {
            return null;
        }

        using var source = streamInfo.Stream;
        using var output = new MemoryStream();
        source.CopyTo(output);
        using var iconStream = new MemoryStream(output.ToArray());
        using var icon = new Icon(iconStream);
        return (Icon)icon.Clone();
    }

    private static T? SafeLoad<T>(Func<T?> factory) where T : class
    {
        try
        {
            return factory();
        }
        catch
        {
            return null;
        }
    }
}
