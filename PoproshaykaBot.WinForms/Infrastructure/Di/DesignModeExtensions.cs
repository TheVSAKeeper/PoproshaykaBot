using System.ComponentModel;
using System.Diagnostics;

namespace PoproshaykaBot.WinForms.Infrastructure.Di;

public static class DesignModeExtensions
{
    public static bool IsInDesignMode(this Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        for (var current = control; current is not null; current = current.Parent)
        {
            if (current.Site?.DesignMode == true)
            {
                return true;
            }
        }

        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
        {
            return true;
        }

        var processName = Process.GetCurrentProcess().ProcessName;

        return string.Equals(processName, "devenv", StringComparison.OrdinalIgnoreCase);
    }
}
