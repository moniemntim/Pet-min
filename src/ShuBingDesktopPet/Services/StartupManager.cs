using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace ShuBingDesktopPet.Services;

public sealed class StartupManager
{
    private static readonly string StartupFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft\\Windows\\Start Menu\\Programs\\Startup");

    public string ShortcutPath => Path.Combine(StartupFolder, "ShuBingDesktopPet.lnk");

    public void SetStartWithWindows(bool enabled, string executablePath)
    {
        Directory.CreateDirectory(StartupFolder);
        if (enabled)
        {
            CreateOrRepairShortcut(executablePath);
            return;
        }

        if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }
    }

    private void CreateOrRepairShortcut(string executablePath)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            return;
        }

        object? shellObj = null;
        object? shortcutObj = null;
        try
        {
            shellObj = Activator.CreateInstance(shellType);
            if (shellObj == null)
            {
                return;
            }

            dynamic shell = shellObj;
            shortcutObj = shell.CreateShortcut(ShortcutPath);
            if (shortcutObj == null)
            {
                return;
            }

            dynamic shortcut = shortcutObj;
            shortcut.TargetPath = executablePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            shortcut.Description = "薯餅桌寵";
            shortcut.WindowStyle = 7;
            shortcut.Save();
        }
        finally
        {
            ReleaseComObject(shortcutObj);
            ReleaseComObject(shellObj);
        }
    }

    private static void ReleaseComObject(object? obj)
    {
        if (obj == null || !Marshal.IsComObject(obj))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(obj);
        }
        catch
        {
            // Ensure release failures do not hide the original exception.
        }
    }
}
