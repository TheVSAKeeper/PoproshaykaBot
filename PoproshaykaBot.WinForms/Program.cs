namespace PoproshaykaBot.WinForms;

public static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var composition = new Composition();

        var settingsManager = composition.SettingsManager;
        var statistics = composition.StatisticsCollector;

        var twitchSettings = settingsManager.Current.Twitch;
        var httpServerEnabled = twitchSettings.HttpServerEnabled;

        if (httpServerEnabled)
        {
            var portValidator = composition.PortValidator;
            var portValidationPassed = portValidator.ValidateAndResolvePortConflict();

            if (portValidationPassed)
            {
                try
                {
                    var httpServer = composition.UnifiedHttpServer;
                    httpServer.StartAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка запуска HTTP сервера: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("Не удалось разрешить конфликт портов. HTTP сервер не запущен.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        statistics.LoadStatisticsAsync().GetAwaiter().GetResult();

        Application.Run(composition.MainForm);
    }
}
