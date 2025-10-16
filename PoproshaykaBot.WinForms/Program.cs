using PoproshaykaBot.Core;
using PoproshaykaBot.Core.Infrastructure.Http;

namespace PoproshaykaBot.WinForms;

public static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var compositionRoot = new ApplicationCompositionRoot();

        var twitchSettings = compositionRoot.SettingsManager.Current.Twitch;
        var httpServerEnabled = twitchSettings.HttpServerEnabled;
        var portValidationPassed = false;

        if (httpServerEnabled)
        {
            var portConflictDialogService = new PortConflictDialogService();
            var portValidator = new PortValidator(compositionRoot.SettingsManager, portConflictDialogService);
            portValidationPassed = portValidator.ValidateAndResolvePortConflict();
        }

        if (httpServerEnabled && portValidationPassed)
        {
            try
            {
                compositionRoot.HttpServer.StartAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска HTTP сервера: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        else if (httpServerEnabled && !portValidationPassed)
        {
            MessageBox.Show("Не удалось разрешить конфликт портов. HTTP сервер не запущен.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        compositionRoot.StatisticsCollector.LoadStatisticsAsync().GetAwaiter().GetResult();

        using var mainForm = new MainForm(
            compositionRoot.ChatHistoryManager,
            compositionRoot.HttpServer,
            compositionRoot.ConnectionManager,
            compositionRoot.SettingsManager,
            compositionRoot.OAuthService,
            compositionRoot.StatisticsCollector);

        Application.Run(mainForm);
    }
}
