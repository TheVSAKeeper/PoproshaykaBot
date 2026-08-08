using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class ChatDisplayTileView : UserControl, IView<ChatDisplayTileViewModel>
{
    private bool _initialized;
    private bool _reloadAttempted;
    private ChatDisplayTileViewModel? _viewModel;
    private string? _clutterScriptId;

    public ChatDisplayTileView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        if (DataContext is not ChatDisplayTileViewModel viewModel)
        {
            return;
        }

        _initialized = true;
        _viewModel = viewModel;
        _viewModel.ReloadRequested += OnReloadRequested;
        _viewModel.ResetSessionRequested += OnResetSessionRequested;
        _viewModel.ClutterScriptChanged += OnClutterScriptChanged;

        await InitializeWebViewAsync(viewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ReloadRequested -= OnReloadRequested;
            _viewModel.ResetSessionRequested -= OnResetSessionRequested;
            _viewModel.ClutterScriptChanged -= OnClutterScriptChanged;
        }

        if (WebView.CoreWebView2 is not null)
        {
            WebView.CoreWebView2.ProcessFailed -= OnProcessFailed;
        }

        WebView.NavigationCompleted -= OnNavigationCompleted;
        WebView.Dispose();
    }

    private async Task InitializeWebViewAsync(ChatDisplayTileViewModel viewModel)
    {
        try
        {
            var environment = await WebView2EnvironmentFactory.CreateAsync(viewModel.UserDataFolder, viewModel.Logger);
            await WebView.EnsureCoreWebView2Async(environment);

            WebView.CoreWebView2.ProcessFailed += OnProcessFailed;
            WebView.NavigationCompleted += OnNavigationCompleted;

            await RegisterClutterScriptAsync(viewModel);

            if (viewModel.ChannelUri is { } channelUri)
            {
                WebView.Source = channelUri;
            }
            else
            {
                viewModel.ShowNoChannelFallback();
            }
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            viewModel.Logger.LogError(exception, "WebView2 Runtime не установлен");
            viewModel.ShowRuntimeMissingFallback();
        }
        catch (Exception exception)
        {
            viewModel.Logger.LogError(exception, "Не удалось инициализировать WebView2 для Twitch-чата");
            viewModel.ShowGenericFallback();
        }
    }

    private async Task RegisterClutterScriptAsync(ChatDisplayTileViewModel viewModel)
    {
        if (WebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            if (_clutterScriptId is not null)
            {
                WebView.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_clutterScriptId);
            }

            _clutterScriptId = await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(viewModel.BuildHideClutterScript());
        }
        catch (Exception exception)
        {
            viewModel.Logger.LogWarning(exception, "Не удалось применить блокираторы баннеров чата");
        }
    }

    private async void OnClutterScriptChanged()
    {
        if (_viewModel is null || WebView.CoreWebView2 is null)
        {
            return;
        }

        await RegisterClutterScriptAsync(_viewModel);

        WebView.Reload();
    }

    private void OnReloadRequested()
    {
        if (WebView.CoreWebView2 is not null)
        {
            WebView.Reload();
        }
    }

    private async void OnResetSessionRequested()
    {
        if (_viewModel is null || WebView.CoreWebView2?.Profile is null)
        {
            return;
        }

        var confirmed = StyledMessageBox.Show(
            $"Это разлогинит {_viewModel.AccountLabel} из Twitch-чата и очистит куки профиля. Продолжить?",
            "Сброс сессии Twitch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            return;
        }

        try
        {
            await WebView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite);
            WebView.Reload();
        }
        catch (Exception exception)
        {
            _viewModel.Logger.LogError(exception, "Не удалось очистить данные сессии WebView2");
        }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _viewModel?.Logger.LogError("WebView2 процесс упал: {Reason}", e.ProcessFailedKind);

        if (_reloadAttempted)
        {
            return;
        }

        _reloadAttempted = true;

        Dispatcher.BeginInvoke(() =>
        {
            if (WebView.CoreWebView2 is not null)
            {
                WebView.Reload();
            }
        });
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            _reloadAttempted = false;
            return;
        }

        _viewModel?.Logger.LogWarning("Twitch-чат не загрузился: status={HttpStatusCode}, error={WebErrorStatus}", e.HttpStatusCode, e.WebErrorStatus);
    }
}
