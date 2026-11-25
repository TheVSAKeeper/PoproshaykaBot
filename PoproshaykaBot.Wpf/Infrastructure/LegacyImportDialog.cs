using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Migration;
using PoproshaykaBot.Wpf.Views.Migration;
using System.Windows;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed class LegacyImportDialog(
    IFilePicker filePicker,
    StatisticsAutoSaver statisticsAutoSaver,
    SettingsWriteGate settingsWriteGate,
    UiSettingsWriteGuard uiSettingsGuard,
    ChatHistoryCacheStore chatHistoryCache,
    ILogger<LegacyImportDialog> logger) : ILegacyImportDialog
{
    public LegacyImportResult? Show()
    {
        if (App.IsHeadless)
        {
            logger.LegacyImportSuppressedHeadless();
            return null;
        }

        var viewModel = new LegacyImportViewModel(
            [],
            null,
            isSettingsEntry: true,
            filePicker,
            logger,
            statisticsAutoSaver,
            settingsWriteGate,
            uiSettingsGuard,
            chatHistoryCache);

        var window = new LegacyImportWindow(viewModel)
        {
            Owner = DialogOwner.Resolve(),
        };
        window.Loaded += LoadSources;

        window.ShowDialog();

        return viewModel.Result;

        async void LoadSources(object sender, RoutedEventArgs args)
        {
            window.Loaded -= LoadSources;
            await viewModel.LoadSourcesAsync();
        }
    }
}
