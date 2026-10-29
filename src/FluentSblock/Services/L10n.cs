using System.Globalization;
using System.Resources;

namespace FluentSblock.Services;

public static class L10n
{
    private static readonly ResourceManager Rm =
        new("FluentSblock.Resources.Strings", typeof(L10n).Assembly);

    public static string Get(string key)
    {
        try
        {
            return Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }
        catch { return key; }
    }

    public static void SetLanguage(string culture)
    {
        try { CultureInfo.CurrentUICulture = new CultureInfo(culture); }
        catch { }
    }
}
