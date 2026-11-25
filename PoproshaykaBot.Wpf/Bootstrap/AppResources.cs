namespace PoproshaykaBot.Wpf.Bootstrap;

internal static class AppResources
{
    public static readonly Uri[] All =
    [
        new("pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml", UriKind.Absolute),
        new("pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml", UriKind.Absolute),
        new("pack://application:,,,/KeepShell;component/Resources/Converters.xaml", UriKind.Absolute),
        new("pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml", UriKind.Absolute),
        new("pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml", UriKind.Absolute),
        new("pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/TableStyles.xaml", UriKind.Absolute),
        new("pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/RatingStyles.xaml", UriKind.Absolute),
        new("pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/PlatformIcons.xaml", UriKind.Absolute),
    ];
}
