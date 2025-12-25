using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms;

public sealed class PortValidator(SettingsManager settingsManager)
{
    public bool ValidateAndResolvePortConflict()
    {
        var settings = settingsManager.Current;
        var redirectUri = settings.Twitch.RedirectUri;
        var serverPort = settings.Twitch.HttpServerPort;

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            MessageBox.Show($"Некорректный RedirectUri: {redirectUri}\n\nПожалуйста, исправьте URI в настройках OAuth.",
                "Ошибка конфигурации",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return false;
        }

        int redirectPort;
        if (uri.Port == -1)
        {
            redirectPort = uri.Scheme == "https" ? 443 : 80;
        }
        else
        {
            redirectPort = uri.Port;
        }

        if (redirectPort == serverPort)
        {
            return true;
        }

        settings.Twitch.HttpServerPort = redirectPort;
        settingsManager.SaveSettings(settings);

        var message = $"""
                       Обнаружен конфликт портов:

                       • RedirectUri использует порт: {redirectPort}
                       • HTTP сервер был настроен на порт: {serverPort}

                       Для корректной работы OAuth порт HTTP сервера был автоматически обновлен до {redirectPort}.

                       Если вы хотите использовать другой порт, пожалуйста, измените его вручную в настройках HTTP сервера и RedirectUri.
                       """;

        MessageBox.Show(message,
            "Порт обновлен",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        return true;
    }
}
