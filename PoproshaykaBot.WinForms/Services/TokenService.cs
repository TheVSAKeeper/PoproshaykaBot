using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Services;

public sealed class TokenService(
    SettingsManager settingsManager,
    TwitchOAuthService oauthService,
    UnifiedHttpServer httpServer)
{
    public async Task<string?> GetAccessTokenAsync()
    {
        var settings = settingsManager.Current.Twitch;

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            MessageBox.Show("OAuth настройки не настроены (ClientId/ClientSecret).", "Ошибка конфигурации OAuth", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            if (await oauthService.IsTokenValidAsync(settings.AccessToken))
            {
                Console.WriteLine("Используется сохранённый токен доступа.");
                return settings.AccessToken;
            }

            if (!string.IsNullOrWhiteSpace(settings.RefreshToken))
            {
                try
                {
                    Console.WriteLine("Обновление токена доступа...");

                    var validToken = await oauthService.GetValidTokenAsync(settings.ClientId,
                        settings.ClientSecret,
                        settings.AccessToken,
                        settings.RefreshToken);

                    Console.WriteLine("Токен доступа обновлён.");
                    return validToken;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось обновить токен доступа: {ex.Message}", "Ошибка обновления токена", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        var serverStartedByUs = false;
        try
        {
            if (!httpServer.IsRunning)
            {
                await httpServer.StartAsync();
                serverStartedByUs = true;
            }

            var accessToken = await oauthService.StartOAuthFlowAsync(settings.ClientId,
                settings.ClientSecret,
                settings.Scopes,
                settings.RedirectUri);

            Console.WriteLine("OAuth авторизация завершена успешно.");
            return accessToken;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"OAuth авторизация не удалась: {ex.Message}", "Ошибка OAuth авторизации", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        finally
        {
            if (serverStartedByUs)
            {
                await httpServer.StopAsync();
            }
        }
    }
}
