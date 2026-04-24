using System.ComponentModel;

namespace PoproshaykaBot.WinForms.Infrastructure.Di;

public static class DesignModeExtensions
{
    public static bool IsInDesignMode(this Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return control.Site?.DesignMode == true
            || LicenseManager.UsageMode == LicenseUsageMode.Designtime;
    }
}
