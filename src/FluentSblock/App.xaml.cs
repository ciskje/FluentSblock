using System.Globalization;
using System.IO;
using System.Windows;

namespace FluentSblock;

public partial class App : Application
{
    /// <summary>Culture passed via --lang= (wins over saved settings).</summary>
    public static string StartupLang { get; private set; } = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        // --lang=fr override for testing EFIGS.
        // --user (set by the Admin→User switch) opts out of auto-elevation.
        bool stayUser = false;
        foreach (var arg in e.Args)
        {
            if (arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
            {
                var lang = arg["--lang=".Length..].Trim();
                try { CultureInfo.CurrentUICulture = new CultureInfo(lang); StartupLang = lang; }
                catch (CultureNotFoundException) { /* fallback to OS */ }
            }
            else if (arg.Equals("--user", StringComparison.OrdinalIgnoreCase))
            {
                stayUser = true;
            }
        }

        // Default to Admin: relaunch elevated via UAC unless opted out.
        // A requireAdministrator manifest cannot be used instead: it would
        // re-elevate every launch, making "Switch to User" impossible.
        if (!stayUser && !Services.ThemeService.IsElevated())
        {
            if (Services.ElevationService.RestartAsAdmin())
            {
                Shutdown();
                return;
            }
            // UAC denied: continue as standard user.
        }

        try { SetCurrentProcessExplicitAppUserModelId("FluentSblock"); }
        catch { /* taskbar grouping best effort */ }

        FluentSblock.Services.ThemeService.ApplySystemAccent();
        base.OnStartup(e);
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelId(string appId);
}
