namespace PoproshaykaBot.Core;

public sealed class PortValidator(SettingsManager settingsManager, IPortConflictDialogService dialogService)
{
    public bool ValidateAndResolvePortConflict()
    {
        var settings = settingsManager.Current;
        var redirectUri = settings.Twitch.RedirectUri;
        var serverPort = settings.Twitch.HttpServerPort;

        if (Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) == false)
        {
            dialogService.ShowInvalidRedirectUri(redirectUri);
            return false;
        }

        var redirectPort = uri.Port == -1 ? uri.Scheme == "https" ? 443 : 80 : uri.Port;

        if (redirectPort == serverPort)
        {
            return true;
        }

        var result = dialogService.ShowPortConflictDialog(redirectPort, serverPort);

        switch (result)
        {
            case PortConflictResolution.UseRedirectPort:
                settings.Twitch.HttpServerPort = redirectPort;
                break;

            case PortConflictResolution.UpdateRedirectUri:
                settings.Twitch.RedirectUri = $"{uri.Scheme}://localhost:{serverPort}";
                break;

            case PortConflictResolution.OpenSettings:
                return false;
        }

        settingsManager.SaveSettings(settings);
        return true;
    }
}
