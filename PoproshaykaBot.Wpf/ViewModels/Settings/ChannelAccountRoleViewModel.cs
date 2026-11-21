using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.ViewModels;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed record ChannelAccountOption(
    string? AccountId,
    string DisplayName,
    string ContextId,
    bool HasRoleGrant = true)
{
    public bool IsUnassigned => string.IsNullOrWhiteSpace(AccountId);
}

public sealed partial class ChannelAccountRoleViewModel : ObservableObject
{
    private const string ReauthorizationNotice = "Войдите в назначенный аккаунт заново.";
    private const string NeedsAuthSuffix = " · нужен вход";
    private const string NeedsBotGrantSuffix = " · нужны права бота";
    private const string NeedsBroadcasterGrantSuffix = " · нужны права стримера";

    private static readonly ChannelAccountOption UnassignedOption =
        new(null, "Не назначен", AuthContextIds.Default, HasRoleGrant: true);

    private readonly Action _onEdited;
    private readonly Func<ChannelAccountRoleViewModel, Task> _verify;
    private readonly Func<ChannelAccountRoleViewModel, Task> _authorize;
    private (string? Account, string Context, string? Verified) _saved;
    private string? _verifiedId;
    private bool _supportsAccounts;
    private bool _canVerify;
    private bool _hasRoleGrant = true;

    [ObservableProperty]
    private IReadOnlyList<ChannelAccountOption> _accounts = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AuthorizeCommand), nameof(VerifyCommand), nameof(ClearAssignmentCommand))]
    [NotifyPropertyChangedFor(nameof(CanSelectAccount))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private StatusSeverity _statusSeverity;

    public ChannelAccountRoleViewModel(
        ChannelCatalogEntry entry,
        PlatformAuthRole role,
        Action onEdited,
        Func<ChannelAccountRoleViewModel, Task> verify,
        Func<ChannelAccountRoleViewModel, Task> authorize)
    {
        Role = role;
        _onEdited = onEdited;
        _verify = verify;
        _authorize = authorize;
        var assignment = entry.GetAccountAssignment(role);
        ContextId = assignment?.ContextId ?? AuthContextIds.Normalize(role == PlatformAuthRole.Bot ? entry.BotContextId : entry.BroadcasterContextId);
        AccountId = assignment?.Account.AccountId;
        _verifiedId = assignment?.IsVerified == true ? AccountId : null;
        AcceptEdits();
        _status = AccountId is null
            ? DefaultUnassignedNotice()
            : _verifiedId is null ? "Требуется проверка" : "Доступ подтверждён";
        _statusSeverity = AccountId is null
            ? (Role == PlatformAuthRole.Bot ? StatusSeverity.Warning : StatusSeverity.None)
            : (_verifiedId is null ? StatusSeverity.Warning : StatusSeverity.Success);
    }

    public PlatformAuthRole Role { get; }

    public string Title => Role == PlatformAuthRole.Bot ? "Бот" : "Стример";

    public string AuthorizationLabel => NeedsRoleGrant
        ? (Role == PlatformAuthRole.Bot ? "Выдать права бота" : "Выдать права стримера")
        : (Role == PlatformAuthRole.Bot ? "Войти как бот" : "Войти как стример");

    public string AuthorizationAutomationId => Role == PlatformAuthRole.Bot ? "ChannelBotAuthorization" : "ChannelBroadcasterAuthorization";

    public string ClearAutomationId => Role == PlatformAuthRole.Bot ? "ChannelBotClearAssignment" : "ChannelBroadcasterClearAssignment";

    public string ContextId { get; private set; }

    public string? AccountId { get; private set; }

    public bool HasAssignedAccount => !string.IsNullOrWhiteSpace(AccountId);

    public bool HasRoleGrant => _hasRoleGrant;

    public bool NeedsRoleGrant => HasAssignedAccount && !_hasRoleGrant;

    public bool CanAuthorize { get; private set; }

    public bool CanManageAccount => CanAuthorize || _supportsAccounts;

    public bool CanSelectAccount => _canVerify && !IsBusy && Accounts.Count > 0;

    public bool NeedsVerification => HasAssignedAccount && (_verifiedId != AccountId || !_hasRoleGrant);

    internal bool IsEdited => _saved != (AccountId, ContextId, _verifiedId);

    internal void AcceptEdits()
    {
        _saved = (AccountId, ContextId, _verifiedId);
        OnPropertyChanged(nameof(NeedsVerification));
    }

    public string? AccountDisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AccountId))
            {
                return null;
            }

            var display = SelectedAccount?.DisplayName;
            if (string.IsNullOrWhiteSpace(display))
            {
                return AccountId;
            }

            return StripDisplaySuffix(display);
        }
    }

    public ChannelAccountOption? SelectedAccount
    {
        get => string.IsNullOrWhiteSpace(AccountId)
            ? Accounts.FirstOrDefault(static option => option.IsUnassigned)
            : Accounts.FirstOrDefault(option => !option.IsUnassigned && string.Equals(option.AccountId, AccountId, StringComparison.Ordinal));
        set
        {
            if (value is null || value.IsUnassigned)
            {
                ClearAssignment();
                return;
            }

            Assign(value.AccountId!, value.ContextId, value.HasRoleGrant);
        }
    }

    public void RefreshAccounts(IReadOnlyList<PlatformAccount>? accounts, bool canAuthorize, bool canVerify)
    {
        CanAuthorize = canAuthorize;
        _supportsAccounts = accounts is not null;
        _canVerify = canVerify && _supportsAccounts;
        if (accounts is null)
        {
            Accounts = [];
            _hasRoleGrant = true;
            SetStatus(string.Empty, StatusSeverity.None);
            NotifyAccountOptionsChanged();
            return;
        }

        var options = new List<ChannelAccountOption> { UnassignedOption };
        var hasAssignedAuthorization = false;
        foreach (var account in accounts)
        {
            var grants = account.Authorizations.Where(grant => grant.Role == Role).ToArray();
            var current = grants.FirstOrDefault(grant => grant.ContextId == ContextId);
            var grant = account.Key.AccountId == AccountId && current is not null
                ? current
                : grants.FirstOrDefault(value => value.HasToken) ?? grants.FirstOrDefault();
            if (grant is not null)
            {
                if (account.Key.AccountId == AccountId && grant.ContextId == ContextId)
                {
                    hasAssignedAuthorization = grant.HasToken;
                }

                var label = grant.HasToken ? account.DisplayName : $"{account.DisplayName}{NeedsAuthSuffix}";
                options.Add(new(account.Key.AccountId, label, grant.ContextId, HasRoleGrant: grant.HasToken));
                continue;
            }

            var otherGrant = account.Authorizations.FirstOrDefault(value => value.Role != Role && value.HasToken);
            if (otherGrant is not null)
            {
                var suffix = Role == PlatformAuthRole.Bot ? NeedsBotGrantSuffix : NeedsBroadcasterGrantSuffix;
                options.Add(new(account.Key.AccountId, $"{account.DisplayName}{suffix}", string.Empty, HasRoleGrant: false));
            }
        }

        var selected = string.IsNullOrWhiteSpace(AccountId)
            ? null
            : options.FirstOrDefault(option => !option.IsUnassigned && option.AccountId == AccountId);

        if (AccountId is { } assigned)
        {
            if (selected is null)
            {
                _hasRoleGrant = false;
                SetStatus(ReauthorizationNotice, StatusSeverity.Warning);
                options.Add(new(assigned, $"{assigned}{NeedsAuthSuffix}", ContextId, HasRoleGrant: false));
            }
            else if (!selected.HasRoleGrant)
            {
                _hasRoleGrant = false;
                if (string.IsNullOrEmpty(selected.ContextId))
                {
                    SetStatus(MissingRoleGrantNotice(StripDisplaySuffix(selected.DisplayName)), StatusSeverity.Warning);
                }
                else
                {
                    SetStatus(ReauthorizationNotice, StatusSeverity.Warning);
                }
            }
            else if (string.IsNullOrEmpty(ContextId))
            {
                ContextId = selected.ContextId;
                _hasRoleGrant = true;
                SetStatus(NeedsVerification ? "Требуется проверка" : "Доступ подтверждён",
                    NeedsVerification ? StatusSeverity.Warning : StatusSeverity.Success);
            }
            else if (selected.ContextId != ContextId || !hasAssignedAuthorization)
            {
                _hasRoleGrant = false;
                SetStatus(ReauthorizationNotice, StatusSeverity.Warning);
            }
            else
            {
                _hasRoleGrant = true;
                if (Status == ReauthorizationNotice || Status.Contains("Нажмите «Выдать права", StringComparison.Ordinal))
                {
                    SetStatus(NeedsVerification ? "Требуется проверка" : "Доступ подтверждён",
                        NeedsVerification ? StatusSeverity.Warning : StatusSeverity.Success);
                }
            }
        }
        else
        {
            _hasRoleGrant = true;
            SetStatus(DefaultUnassignedNotice(),
                Role == PlatformAuthRole.Bot ? StatusSeverity.Warning : StatusSeverity.None);
        }

        Accounts = options;
        NotifyAccountOptionsChanged();
    }

    private void NotifyAccountOptionsChanged()
    {
        OnPropertyChanged(nameof(AccountId));
        OnPropertyChanged(nameof(HasAssignedAccount));
        OnPropertyChanged(nameof(HasRoleGrant));
        OnPropertyChanged(nameof(NeedsRoleGrant));
        OnPropertyChanged(nameof(AuthorizationLabel));
        OnPropertyChanged(nameof(AccountDisplayName));
        OnPropertyChanged(nameof(NeedsVerification));
        OnPropertyChanged(nameof(CanAuthorize));
        OnPropertyChanged(nameof(CanManageAccount));
        OnPropertyChanged(nameof(CanSelectAccount));
        OnPropertyChanged(nameof(SelectedAccount));
        AuthorizeCommand.NotifyCanExecuteChanged();
        VerifyCommand.NotifyCanExecuteChanged();
        ClearAssignmentCommand.NotifyCanExecuteChanged();
    }

    public void Assign(string accountId, string contextId, bool hasRoleGrant = true)
    {
        if (AccountId == accountId && ContextId == contextId && _hasRoleGrant == hasRoleGrant)
        {
            return;
        }

        AccountId = accountId;
        ContextId = contextId;
        _verifiedId = null;
        _hasRoleGrant = hasRoleGrant;

        if (!hasRoleGrant)
        {
            var name = AccountDisplayName ?? accountId;
            SetStatus(string.IsNullOrEmpty(contextId)
                ? MissingRoleGrantNotice(name)
                : ReauthorizationNotice,
                StatusSeverity.Warning);
        }
        else
        {
            SetStatus("Требуется проверка", StatusSeverity.Warning);
        }

        NotifyAccountOptionsChanged();
        _onEdited();
    }

    public void SetVerification(ChannelAccessVerification result)
    {
        var verified = result.IsAllowed && !string.IsNullOrWhiteSpace(result.SubjectId)
            && !string.IsNullOrWhiteSpace(AccountId)
            && AccountId == result.SubjectId;
        var verifiedId = verified ? result.SubjectId : null;
        SetStatus(verified ? "Доступ подтверждён" : result.FailureReason ?? "Площадка не подтвердила доступ назначенного аккаунта",
            verified ? StatusSeverity.Success : StatusSeverity.Error);
        if (_verifiedId != verifiedId)
        {
            _verifiedId = verifiedId;
            OnPropertyChanged(nameof(NeedsVerification));
            OnPropertyChanged(nameof(AccountDisplayName));
            OnPropertyChanged(nameof(SelectedAccount));
            _onEdited();
        }
    }

    public void SetStatus(string message, StatusSeverity severity)
    {
        StatusSeverity = severity;
        Status = message;
    }

    public void ApplyTo(ChannelCatalogEntry entry)
    {
        var normalizedContext = string.IsNullOrWhiteSpace(AccountId) || string.IsNullOrWhiteSpace(ContextId)
            ? AuthContextIds.Default
            : ContextId;

        if (Role == PlatformAuthRole.Bot)
        {
            entry.BotAccountId = AccountId;
            entry.BotContextId = normalizedContext;
            entry.BotVerifiedSubjectId = _verifiedId;
        }
        else
        {
            entry.BroadcasterAccountId = AccountId;
            entry.BroadcasterContextId = normalizedContext;
            entry.BroadcasterVerifiedSubjectId = _verifiedId;
        }
    }

    private string DefaultUnassignedNotice() => Role == PlatformAuthRole.Bot
        ? "Войдите как бот или выберите аккаунт бота и проверьте доступ."
        : "При необходимости выберите аккаунт стримера и проверьте доступ.";

    private string MissingRoleGrantNotice(string displayName) => Role == PlatformAuthRole.Bot
        ? $"Аккаунт «{displayName}» вошёл только как стример. Нажмите «Выдать права бота», чтобы использовать его в этой роли."
        : $"Аккаунт «{displayName}» вошёл только как бот. Нажмите «Выдать права стримера», чтобы использовать его в этой роли.";

    private static string StripDisplaySuffix(string display)
    {
        string[] suffixes = [NeedsAuthSuffix, NeedsBotGrantSuffix, NeedsBroadcasterGrantSuffix];
        foreach (var suffix in suffixes)
        {
            if (display.EndsWith(suffix, StringComparison.Ordinal))
            {
                return display[..^suffix.Length];
            }
        }

        return display;
    }

    private bool CanAuthorizeNow() => CanAuthorize && !IsBusy;

    private bool CanVerifyNow() => _canVerify && !IsBusy && HasAssignedAccount && _hasRoleGrant;

    private bool CanClearAssignment() => _canVerify && !IsBusy && HasAssignedAccount;

    [RelayCommand(CanExecute = nameof(CanClearAssignment))]
    public void ClearAssignment()
    {
        if (AccountId is null && ContextId == AuthContextIds.Default && _verifiedId is null)
        {
            return;
        }

        AccountId = null;
        ContextId = AuthContextIds.Default;
        _verifiedId = null;
        _hasRoleGrant = true;
        SetStatus(DefaultUnassignedNotice(),
            Role == PlatformAuthRole.Bot ? StatusSeverity.Warning : StatusSeverity.None);
        NotifyAccountOptionsChanged();
        _onEdited();
    }

    [RelayCommand(CanExecute = nameof(CanAuthorizeNow))]
    private async Task AuthorizeAsync()
    {
        IsBusy = true;
        try
        {
            await _authorize(this);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanVerifyNow))]
    private async Task VerifyAsync()
    {
        IsBusy = true;
        try
        {
            await _verify(this);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
