using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Net.Http;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class PlatformOAuthAccountViewModel : ObservableObject, IDisposable
{
    private readonly IPlatformAccountAuthAdapter _adapter;
    private readonly IDialogService _dialogService;
    private readonly IShellLauncher _shellLauncher;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    private CancellationTokenSource? _authCts;
    private bool _suppressUpdates;
    private bool _disposed;

    [ObservableProperty]
    private string _scopesText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccessToken))]
    [NotifyPropertyChangedFor(nameof(HasToken))]
    [NotifyPropertyChangedFor(nameof(HasAnyCredential))]
    [NotifyPropertyChangedFor(nameof(OpenBrowserButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ValidateTokenCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _accessTokenValue = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRefreshToken))]
    [NotifyPropertyChangedFor(nameof(HasAnyCredential))]
    [NotifyCanExecuteChangedFor(nameof(RefreshTokenCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _refreshTokenValue = string.Empty;

    [ObservableProperty]
    private string _loginText = "–";

    [ObservableProperty]
    private string _tokenStatusText = string.Empty;

    [ObservableProperty]
    private StatusSeverity _tokenStatusSeverity;

    [ObservableProperty]
    private string _lastRefreshText = "Неизвестно";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChannelIdentity))]
    [NotifyPropertyChangedFor(nameof(ChannelDisplay))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _channelId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChannelIdentity))]
    [NotifyPropertyChangedFor(nameof(ChannelDisplay))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _channelTitle = string.Empty;

    [ObservableProperty]
    private string _channelDisplay = string.Empty;

    [ObservableProperty]
    private string _authorizationSummary = "Не авторизован";

    [ObservableProperty]
    private string _expectedScopesSummary = string.Empty;

    [ObservableProperty]
    private string _grantedScopesSummary = "–";

    [ObservableProperty]
    private bool _hasScopeMismatch;

    [ObservableProperty]
    private string _tokenExpirySummary = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _statusSeverity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmbeddedAuthorizeButton))]
    [NotifyPropertyChangedFor(nameof(ShowPrimaryBrowserAuthorizeButton))]
    [NotifyPropertyChangedFor(nameof(ShowSecondaryBrowserAuthorizeButton))]
    [NotifyCanExecuteChangedFor(nameof(OpenBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(AuthorizeInAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelAuthorizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetScopesCommand))]
    private bool _isAuthorizing;

    public PlatformOAuthAccountViewModel(
        IPlatformAccountAuthAdapter adapter,
        IDialogService dialogService,
        IShellLauncher shellLauncher,
        IUiDispatcher uiDispatcher,
        TimeProvider timeProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(shellLauncher);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _adapter = adapter;
        _dialogService = dialogService;
        _shellLauncher = shellLauncher;
        _uiDispatcher = uiDispatcher;
        _timeProvider = timeProvider;
        _logger = logger;

        var display = _adapter.DisplayOptions;
        Capabilities = new(
            SupportsEditableScopes: _adapter is IEditableScopesAccountAuthAdapter,
            SupportsEmbeddedAuth: _adapter is IEmbeddedAccountAuthAdapter { SupportsEmbeddedAuth: true },
            SupportsTokenValidation: _adapter is ITokenMaintenanceAccountAuthAdapter,
            SupportsManualTokenRefresh: _adapter is ITokenMaintenanceAccountAuthAdapter,
            ShowsRolePurposeHint: display.ShowsRolePurposeHint,
            ShowsAuthorizationSummary: display.ShowsAuthorizationSummary,
            ShowsLoginAndRefreshMetadata: display.ShowsLoginAndRefreshMetadata,
            ShowsChannelIdentity: display.ShowsChannelIdentity,
            ShowsScopeSummaries: display.ShowsScopeSummaries,
            ChannelIdentityLabel: display.ChannelIdentityLabel);

        _adapter.StatusChanged += OnAdapterStatusChanged;
    }

    public event EventHandler? SettingChanged;

    public PlatformAccountAuthCapabilities Capabilities { get; }

    public string RolePurposeHint => _adapter.RolePurposeHint;

    public string EmbeddedAuthorizeButtonText => (_adapter as IEmbeddedAccountAuthAdapter)?.EmbeddedAuthorizeButtonText ?? string.Empty;

    public string OpenBrowserButtonText => _adapter.OpenBrowserButtonText(HasToken);

    public bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessTokenValue);

    public bool HasToken => HasAccessToken;

    public bool HasRefreshToken => !string.IsNullOrWhiteSpace(RefreshTokenValue);

    public bool HasAnyCredential => HasToken || HasRefreshToken;

    public bool HasChannelIdentity =>
        !string.IsNullOrWhiteSpace(ChannelId) || !string.IsNullOrWhiteSpace(ChannelTitle);

    public bool ShowEmbeddedAuthorizeButton => Capabilities.SupportsEmbeddedAuth && !IsAuthorizing;

    public bool ShowPrimaryBrowserAuthorizeButton => !Capabilities.SupportsEmbeddedAuth && !IsAuthorizing;

    public bool ShowSecondaryBrowserAuthorizeButton => Capabilities.SupportsEmbeddedAuth && !IsAuthorizing;

    public void Load(IRoleAccountSettings account)
    {
        ArgumentNullException.ThrowIfNull(account);

        _suppressUpdates = true;
        try
        {
            _adapter.Load(account);
            ScopesText = _adapter.ReadScopesText();
            AccessTokenValue = _adapter.ReadAccessToken();
            RefreshTokenValue = _adapter.ReadRefreshToken();
            SetStatus(string.Empty, StatusSeverity.None);
            RefreshPresentation();
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    public void SaveTo(IRoleAccountSettings target)
    {
        ArgumentNullException.ThrowIfNull(target);

        (_adapter as IEditableScopesAccountAuthAdapter)?.WriteScopesText(ScopesText);
        _adapter.SaveTo(target);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _adapter.StatusChanged -= OnAdapterStatusChanged;
        _authCts?.Cancel();
        _authCts?.Dispose();
        _authCts = null;
    }

    partial void OnScopesTextChanged(string value)
    {
        if (!_suppressUpdates && _adapter is IEditableScopesAccountAuthAdapter editable)
        {
            editable.WriteScopesText(value);
            RaiseSettingChanged();
        }
    }

    partial void OnAccessTokenValueChanged(string value)
    {
        if (!_suppressUpdates)
        {
            _adapter.OnManualTokenEdited(value, RefreshTokenValue);
            RefreshPresentation();
            RaiseSettingChanged();
        }
    }

    partial void OnRefreshTokenValueChanged(string value)
    {
        if (!_suppressUpdates)
        {
            _adapter.OnManualTokenEdited(AccessTokenValue, value);
            RefreshPresentation();
            RaiseSettingChanged();
        }
    }

    private bool CanStartBrowserAuth() => !IsAuthorizing;

    [RelayCommand(CanExecute = nameof(CanStartBrowserAuth))]
    private async Task OpenBrowserAsync()
    {
        if (!_adapter.TryPrepareBrowserAuthorization(out var statusError, out var warningDialog))
        {
            if (statusError is not null)
            {
                SetStatus(statusError, StatusSeverity.Error);
            }

            if (warningDialog is { } dialog)
            {
                _dialogService.Warning(dialog.Title, dialog.Message);
            }

            return;
        }

        _authCts?.Dispose();
        var cts = new CancellationTokenSource();
        _authCts = cts;

        IsAuthorizing = true;
        SetStatus("Авторизация в браузере...", StatusSeverity.Info);
        _logger.PlatformOAuthBrowserFlowStarted(_adapter.PlatformKey, _adapter.Role, OAuthAuthorizationSurface.Settings);

        try
        {
            var outcome = await _adapter.StartBrowserAuthorizationAsync(
                ScopesText,
                OpenInSystemBrowser,
                _timeProvider.GetUtcNow(),
                cts.Token);

            if (_disposed || !ReferenceEquals(_authCts, cts))
            {
                return;
            }

            ApplyFlowOutcome(outcome);
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && ReferenceEquals(_authCts, cts))
            {
                _logger.PlatformOAuthFlowCancelled(_adapter.PlatformKey, _adapter.Role);
                SetStatus("Авторизация отменена", StatusSeverity.Warning);
            }
        }
        catch (TimeoutException exception)
        {
            if (_disposed)
            {
                return;
            }

            if (!ReferenceEquals(_authCts, cts))
            {
                _logger.OAuthAbandonedFlowFailed(exception, _adapter.Role);
                return;
            }

            _logger.PlatformOAuthFlowFailed(exception, _adapter.PlatformKey, _adapter.Role);
            var safe = SafeMessage(exception);
            SetStatus($"Ошибка: {safe}", StatusSeverity.Warning);
            _dialogService.Warning(_adapter.TimeoutDialogTitle, $"Не удалось завершить вход вовремя: {safe}.");
        }
        catch (Exception exception)
        {
            if (_disposed)
            {
                return;
            }

            if (!ReferenceEquals(_authCts, cts))
            {
                _logger.OAuthAbandonedFlowFailed(exception, _adapter.Role);
                return;
            }

            _logger.PlatformOAuthFlowFailed(exception, _adapter.PlatformKey, _adapter.Role);
            var safe = SafeMessage(exception);
            SetStatus($"Ошибка: {safe}", StatusSeverity.Error);
            _dialogService.Error(_adapter.ErrorDialogTitle, $"{_adapter.BrowserErrorDialogHeader}\n\nПричина: {safe}");
        }
        finally
        {
            cts.Dispose();
            if (ReferenceEquals(_authCts, cts))
            {
                _authCts = null;
                if (!_disposed)
                {
                    IsAuthorizing = false;
                }
            }
        }
    }

    private bool CanStartEmbeddedAuth() => Capabilities.SupportsEmbeddedAuth && !IsAuthorizing;

    [RelayCommand(CanExecute = nameof(CanStartEmbeddedAuth))]
    private async Task AuthorizeInAppAsync()
    {
        if (_adapter is not IEmbeddedAccountAuthAdapter embedded)
        {
            return;
        }

        if (!embedded.TryPrepareEmbeddedAuthorization(out var statusError))
        {
            if (statusError is not null)
            {
                SetStatus(statusError, StatusSeverity.Error);
            }

            return;
        }

        IsAuthorizing = true;
        SetStatus("Авторизация во встроенном окне...", StatusSeverity.Info);
        _logger.OAuthEmbeddedFlowStarted(_adapter.Role, OAuthAuthorizationSurface.Settings);

        var switchToBrowser = false;

        try
        {
            var outcome = await embedded.StartEmbeddedAuthorizationAsync(
                ScopesText,
                _timeProvider.GetUtcNow(),
                CancellationToken.None);

            if (_disposed)
            {
                return;
            }

            if (outcome.SwitchToBrowser)
            {
                switchToBrowser = true;
            }
            else
            {
                ApplyFlowOutcome(outcome);
            }
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                _logger.PlatformOAuthFlowFailed(exception, _adapter.PlatformKey, _adapter.Role);
                SetStatus($"Ошибка: {SafeMessage(exception)}", StatusSeverity.Error);
            }
        }
        finally
        {
            if (!_disposed)
            {
                IsAuthorizing = false;
            }
        }

        if (switchToBrowser && !_disposed)
        {
            await OpenBrowserAsync();
        }
    }

    private bool CanCancelAuthorization() => IsAuthorizing;

    [RelayCommand(CanExecute = nameof(CanCancelAuthorization))]
    private async Task CancelAuthorizationAsync()
    {
        if (_authCts is not { } pending)
        {
            return;
        }

        _authCts = null;

        try
        {
            await pending.CancelAsync();
        }
        catch (Exception exception)
        {
            _logger.OAuthAbandonedFlowFailed(exception, _adapter.Role);
        }

        if (!_disposed)
        {
            _logger.PlatformOAuthFlowCancelled(_adapter.PlatformKey, _adapter.Role);
            SetStatus("Авторизация отменена", StatusSeverity.Warning);
            IsAuthorizing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasAccessToken))]
    private async Task ValidateTokenAsync()
    {
        if (_adapter is not ITokenMaintenanceAccountAuthAdapter maintenance)
        {
            return;
        }

        var accessToken = AccessTokenValue;
        TokenStatusText = "Проверка...";
        TokenStatusSeverity = StatusSeverity.Info;

        try
        {
            var outcome = await maintenance.ValidateTokenAsync(accessToken, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            TokenStatusText = outcome.StatusText;
            TokenStatusSeverity = outcome.Severity;
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                _logger.PlatformOAuthValidationFailed(exception, _adapter.PlatformKey, _adapter.Role);
                TokenStatusText = $"Ошибка: {SafeMessage(exception)}";
                TokenStatusSeverity = StatusSeverity.Error;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasRefreshToken))]
    private async Task RefreshTokenAsync()
    {
        if (_adapter is not ITokenMaintenanceAccountAuthAdapter maintenance)
        {
            return;
        }

        if (!maintenance.TryPrepareTokenRefresh(out var warningDialog, out var refreshToken))
        {
            if (warningDialog is { } dialog)
            {
                _dialogService.Warning(dialog.Title, dialog.Message);
            }

            return;
        }

        TokenStatusText = "Обновление...";
        TokenStatusSeverity = StatusSeverity.Info;

        try
        {
            var now = _timeProvider.GetUtcNow();
            var outcome = await maintenance.RefreshTokenAsync(refreshToken, now, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            _suppressUpdates = true;
            try
            {
                AccessTokenValue = outcome.AccessToken;
                RefreshTokenValue = outcome.RefreshToken;
                RefreshPresentation();
            }
            finally
            {
                _suppressUpdates = false;
            }

            TokenStatusText = outcome.TokenStatusText;
            TokenStatusSeverity = outcome.TokenStatusSeverity;
            LastRefreshText = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local)
                .ToString("dd.MM.yyyy HH:mm:ss", UiCulture.Russian);
            RaiseSettingChanged();
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                _logger.PlatformOAuthRefreshFailed(exception, _adapter.PlatformKey, _adapter.Role);
                var safe = SafeMessage(exception);
                TokenStatusText = $"Ошибка: {safe}";
                TokenStatusSeverity = StatusSeverity.Error;
                _dialogService.Error("Ошибка обновления токена", $"Не удалось обновить токен: {safe}");
            }
        }
    }

    private bool CanClear() => !IsAuthorizing && (HasAnyCredential || _adapter.HasAdditionalStateToClear());

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        if (!_dialogService.Confirm(_adapter.ClearConfirmationTitle, _adapter.ClearConfirmationMessage))
        {
            return;
        }

        _suppressUpdates = true;
        try
        {
            _adapter.ClearDraft();
            AccessTokenValue = string.Empty;
            RefreshTokenValue = string.Empty;
            RefreshPresentation();
            LastRefreshText = "Неизвестно";
            TokenStatusText = "Токены очищены (применятся после «Сохранить»)";
            TokenStatusSeverity = StatusSeverity.Warning;
            SetStatus(_adapter.ClearStatusMessage, StatusSeverity.Warning);
        }
        finally
        {
            _suppressUpdates = false;
        }

        _logger.PlatformOAuthTokensCleared(_adapter.PlatformKey, _adapter.Role);
        RaiseSettingChanged();
    }

    private bool CanResetScopes() => !IsAuthorizing;

    [RelayCommand(CanExecute = nameof(CanResetScopes))]
    private void ResetScopes()
    {
        _suppressUpdates = true;
        try
        {
            ScopesText = _adapter.ResetScopes();
            RefreshPresentation();
        }
        finally
        {
            _suppressUpdates = false;
        }

        if (Capabilities.ShowsScopeSummaries)
        {
            SetStatus("Набор запрашиваемых прав сброшен к базовому.", StatusSeverity.Info);
        }

        RaiseSettingChanged();
    }

    private void ApplyFlowOutcome(PlatformAuthFlowOutcome outcome)
    {
        _suppressUpdates = true;
        try
        {
            if (outcome.UpdatedScopesText is not null)
            {
                ScopesText = outcome.UpdatedScopesText;
            }

            AccessTokenValue = _adapter.ReadAccessToken();
            RefreshTokenValue = _adapter.ReadRefreshToken();
            RefreshPresentation();
        }
        finally
        {
            _suppressUpdates = false;
        }

        var severity = outcome.Succeeded
            ? StatusSeverity.Success
            : outcome.IsWarning
                ? StatusSeverity.Warning
                : StatusSeverity.Error;

        SetStatus(outcome.StatusMessage, severity);

        if (outcome.RaiseSettingChanged)
        {
            RaiseSettingChanged();
        }
    }

    private void RefreshPresentation()
    {
        var presentation = _adapter.BuildPresentation(_timeProvider.GetUtcNow());

        AuthorizationSummary = presentation.AuthorizationSummary;
        LoginText = presentation.LoginText;
        TokenStatusText = presentation.TokenStatusText;
        TokenStatusSeverity = presentation.TokenStatusSeverity;
        ChannelId = presentation.ChannelId;
        ChannelTitle = presentation.ChannelTitle;
        ChannelDisplay = presentation.ChannelDisplay;
        ExpectedScopesSummary = presentation.ExpectedScopesSummary;
        GrantedScopesSummary = presentation.GrantedScopesSummary;
        HasScopeMismatch = presentation.HasScopeMismatch;
        TokenExpirySummary = presentation.TokenExpirySummary;
    }

    private void OnAdapterStatusChanged(PlatformAuthRole role, string message)
    {
        if (role != _adapter.Role || _disposed)
        {
            return;
        }

        _ = _uiDispatcher.InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            SetStatus(message, StatusSeverity.Info);
        });
    }

    private void OpenInSystemBrowser(string authUrl)
    {
        if (_disposed)
        {
            return;
        }

        if (!_shellLauncher.Open(authUrl))
        {
            _logger.PlatformOAuthBrowserOpenFailed(_adapter.PlatformKey, _adapter.Role);
        }
    }

    private void SetStatus(string text, StatusSeverity severity)
    {
        StatusMessage = text;
        StatusSeverity = severity;
    }

    private string SafeMessage(Exception exception)
    {
        var adapterMessage = _adapter.FormatSafeError(exception);
        if (!string.IsNullOrWhiteSpace(adapterMessage))
        {
            return adapterMessage;
        }

        return exception switch
        {
            OperationCanceledException => "операция отменена",
            TimeoutException => "превышено время ожидания",
            HttpRequestException => "ошибка сети при обращении к серверу авторизации",
            _ => "не удалось выполнить операцию, проверьте настройки и подключение",
        };
    }

    private void RaiseSettingChanged()
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }
}
