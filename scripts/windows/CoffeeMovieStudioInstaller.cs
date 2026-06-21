using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

internal static class CoffeeMovieStudioInstaller
{
    private const string ProductName = "CoffeeMovie Studio";
    private const string PayloadResourceName = "CoffeeMovieStudio.payload.zip";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        try
        {
            var installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                ProductName);
            var startMenuDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs",
                ProductName);
            var desktopShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                ProductName + ".lnk");

            InstallPayload(installDir);

            var appExe = Path.Combine(installDir, "CoffeeMovie.Studio.exe");
            Directory.CreateDirectory(startMenuDir);
            CreateShortcut(Path.Combine(startMenuDir, ProductName + ".lnk"), appExe, installDir, appExe + ",0");

            var uninstallScript = WriteUninstallScript(installDir, startMenuDir, desktopShortcut);
            CreateShortcut(
                Path.Combine(startMenuDir, "Uninstall " + ProductName + ".lnk"),
                "powershell.exe",
                installDir,
                "powershell.exe,0",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + uninstallScript + "\"");
            CreateShortcut(desktopShortcut, appExe, installDir, appExe + ",0");

            MessageBox.Show(
                ProductName + " をインストールしました。\n\n" + installDir,
                ProductName + " Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "インストールに失敗しました。\n\n" + ex.Message,
                ProductName + " Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Environment.Exit(1);
        }
    }

    private static void InstallPayload(string installDir)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), "CoffeeMovieStudio-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName))
            {
                if (resource == null)
                {
                    throw new InvalidOperationException("インストール用ペイロードが見つかりません。");
                }

                using (var output = File.Create(tempZip))
                {
                    resource.CopyTo(output);
                }
            }

            if (Directory.Exists(installDir))
            {
                Directory.Delete(installDir, true);
            }

            Directory.CreateDirectory(installDir);
            ZipFile.ExtractToDirectory(tempZip, installDir);
        }
        catch (IOException ex)
        {
            throw new IOException(ProductName + " が起動中の場合は閉じてから、もう一度インストーラーを実行してください。", ex);
        }
        finally
        {
            try
            {
                if (File.Exists(tempZip))
                {
                    File.Delete(tempZip);
                }
            }
            catch
            {
                // Temporary cleanup is best effort.
            }
        }
    }

    private static string WriteUninstallScript(string installDir, string startMenuDir, string desktopShortcut)
    {
        var scriptPath = Path.Combine(installDir, "Uninstall-CoffeeMovieStudio.ps1");
        var script =
            "$ErrorActionPreference = 'SilentlyContinue'\r\n" +
            "Add-Type -AssemblyName System.Windows.Forms\r\n" +
            "Remove-Item -LiteralPath '" + EscapePowerShell(installDir) + "' -Recurse -Force\r\n" +
            "Remove-Item -LiteralPath '" + EscapePowerShell(startMenuDir) + "' -Recurse -Force\r\n" +
            "Remove-Item -LiteralPath '" + EscapePowerShell(desktopShortcut) + "' -Force\r\n" +
            "[System.Windows.Forms.MessageBox]::Show('CoffeeMovie Studio をアンインストールしました。','CoffeeMovie Studio Setup') | Out-Null\r\n";
        File.WriteAllText(scriptPath, script, System.Text.Encoding.UTF8);
        return scriptPath;
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''");
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string iconLocation,
        string arguments = "")
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            throw new InvalidOperationException("WScript.Shell を初期化できません。");
        }

        var shell = Activator.CreateInstance(shellType);
        var shortcut = shellType.InvokeMember(
            "CreateShortcut",
            BindingFlags.InvokeMethod,
            null,
            shell,
            new object[] { shortcutPath });
        if (shortcut == null)
        {
            throw new InvalidOperationException("ショートカットを作成できません。");
        }

        var shortcutType = shortcut.GetType();
        shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
        shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
        shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconLocation });
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
        }

        shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, new object[0]);
    }
}
