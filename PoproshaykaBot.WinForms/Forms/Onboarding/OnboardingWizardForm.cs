using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.WinForms.Forms.Onboarding.Pages;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Forms.Onboarding;

public partial class OnboardingWizardForm : Form
{
    private readonly SettingsManager _settingsManager;
    private readonly AccountsStore _accountsStore;
    private readonly IControlFactory _controlFactory;
    private readonly IEventBus _eventBus;
    private readonly KestrelHttpServer _kestrelHttpServer;
    private readonly ILogger<OnboardingWizardForm> _logger;
    private readonly OnboardingContext _context;
    private readonly AppSettings _originalSettings;
    private readonly List<IOnboardingWizardPage> _pages = [];
    private int _currentPageIndex = -1;
    private bool _initialized;
    private bool _settingsCommittedToDisk;
    private bool _completed;
    private bool _revertScheduled;

    public OnboardingWizardForm(
        SettingsManager settingsManager,
        AccountsStore accountsStore,
        IControlFactory controlFactory,
        IEventBus eventBus,
        KestrelHttpServer kestrelHttpServer,
        ILogger<OnboardingWizardForm> logger)
    {
        _settingsManager = settingsManager;
        _accountsStore = accountsStore;
        _controlFactory = controlFactory;
        _eventBus = eventBus;
        _kestrelHttpServer = kestrelHttpServer;
        _logger = logger;

        _originalSettings = DeepClone(settingsManager.Current);
        _context = new(DeepClone(settingsManager.Current),
            DeepClone(accountsStore.LoadBot()),
            DeepClone(accountsStore.LoadBroadcaster()));

        InitializeComponent();
    }

    public void NotifySettingsCommittedToDisk()
    {
        _settingsCommittedToDisk = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_initialized || this.IsInDesignMode())
        {
            return;
        }

        _initialized = true;

        BuildPages();
        NavigateTo(0);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        if (e.Cancel || _revertScheduled || _completed || !_settingsCommittedToDisk)
        {
            return;
        }

        if (DialogResult == DialogResult.OK)
        {
            return;
        }

        _revertScheduled = true;
        e.Cancel = true;
        _ = RevertAndCloseAsync();
    }

    private void OnSaveAccountsRequested(object? sender, EventArgs e)
    {
        try
        {
            _settingsManager.SaveSettings(_context.Settings);
            _accountsStore.SaveAll(_context.BotAccount, _context.BroadcasterAccount);
            _settingsCommittedToDisk = true;
            _completed = true;

            _ = _eventBus.PublishAsync(new TwitchAuthorizationRefreshed(TwitchOAuthRole.Bot));
            _ = _eventBus.PublishAsync(new TwitchAuthorizationRefreshed(TwitchOAuthRole.Broadcaster));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка сохранения настроек wizard'а");
            MessageBox.Show($"Не удалось сохранить настройки: {exception.Message}",
                "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnNextButtonClicked(object? sender, EventArgs e)
    {
        if (_currentPageIndex < 0 || _currentPageIndex >= _pages.Count)
        {
            return;
        }

        var current = _pages[_currentPageIndex];

        SetButtonsEnabled(false);
        try
        {
            var canLeave = await current.OnLeavingAsync(_context);
            if (!canLeave)
            {
                return;
            }
        }
        finally
        {
            SetButtonsEnabled(true);
        }

        if (_currentPageIndex >= _pages.Count - 1)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        NavigateTo(_currentPageIndex + 1);
    }

    private void OnBackButtonClicked(object? sender, EventArgs e)
    {
        if (_currentPageIndex <= 0)
        {
            return;
        }

        NavigateTo(_currentPageIndex - 1);
    }

    private void OnCancelButtonClicked(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void OnCurrentPageCanAdvanceChanged(object? sender, EventArgs e)
    {
        UpdateButtonStates();
    }

    private static T DeepClone<T>(T source) where T : class, new()
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<T>(json) ?? new();
    }

    private void BuildPages()
    {
        _pages.Add(_controlFactory.Create<WelcomePage>());
        _pages.Add(_controlFactory.Create<CredentialsPage>());
        _pages.Add(_controlFactory.Create<HttpServerCheckPage>());

        var botAuth = _controlFactory.Create<AuthorizationPage>();
        botAuth.Role = TwitchOAuthRole.Bot;
        _pages.Add(botAuth);

        var broadcasterAuth = _controlFactory.Create<AuthorizationPage>();
        broadcasterAuth.Role = TwitchOAuthRole.Broadcaster;
        _pages.Add(broadcasterAuth);

        var completion = _controlFactory.Create<CompletionPage>();
        completion.SaveAccountsRequested += OnSaveAccountsRequested;
        _pages.Add(completion);
    }

    private async Task RevertAndCloseAsync()
    {
        try
        {
            var originalPort = _originalSettings.Twitch.HttpServerPort;
            var currentPort = _settingsManager.Current.Twitch.HttpServerPort;

            _settingsManager.SaveSettings(_originalSettings);

            if (originalPort != currentPort && _kestrelHttpServer.IsRunning)
            {
                try
                {
                    await _kestrelHttpServer.StopAsync();
                    await _kestrelHttpServer.StartAsync();
                }
                catch (Exception kestrelEx)
                {
                    _logger.LogError(kestrelEx, "Не удалось вернуть HTTP сервер на прежний порт {Port}", originalPort);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка отката изменений wizard'а");
        }
        finally
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private void NavigateTo(int index)
    {
        if (index < 0 || index >= _pages.Count)
        {
            return;
        }

        if (_currentPageIndex >= 0 && _currentPageIndex < _pages.Count)
        {
            var prev = _pages[_currentPageIndex];
            prev.CanAdvanceChanged -= OnCurrentPageCanAdvanceChanged;
        }

        _pageContainer.Controls.Clear();

        var page = _pages[index];

        if (page is Control control)
        {
            control.Dock = DockStyle.Fill;
            _pageContainer.Controls.Add(control);
        }
        else
        {
            throw new InvalidOperationException("Onboarding wizard page must be a Control");
        }

        page.OnEnter(_context);
        page.CanAdvanceChanged += OnCurrentPageCanAdvanceChanged;

        _currentPageIndex = index;
        _headerLabel.Text = page.PageTitle;
        _stepLabel.Text = $"Шаг {index + 1} из {_pages.Count}";

        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        var isFirst = _currentPageIndex == 0;
        var isLast = _currentPageIndex == _pages.Count - 1;
        var page = _pages[_currentPageIndex];

        _backButton.Enabled = !isFirst;
        _nextButton.Enabled = page.CanAdvance;
        _nextButton.Text = isLast ? "Готово" : "Далее →";
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _backButton.Enabled = enabled && _currentPageIndex > 0;
        _nextButton.Enabled = enabled && _pages[_currentPageIndex].CanAdvance;
        _cancelButton.Enabled = enabled;
    }
}
