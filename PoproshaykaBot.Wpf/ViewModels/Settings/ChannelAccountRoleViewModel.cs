using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.ViewModels;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed record ChannelAccountOption(string AccountId, string DisplayName, string ContextId);

public sealed partial class ChannelAccountRoleViewModel : ObservableObject
{
    private const string ReauthorizationNotice = "Войдите в назначенный аккаунт заново.";

    private readonly Action _onEdited;
    private readonly Func<ChannelAccountRoleViewModel, Task> _verify;
    private readonly Func<ChannelAccountRoleViewModel, Task> _authorize;
    private (string? Account, string Context, string? Verified) _saved;
    private string? _verifiedId;
    private bool _supportsAccounts;
    private bool _canVerify;

    [ObservableProperty]
    private IReadOnlyList<ChannelAccountOption> _accounts = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AuthorizeCommand), nameof(VerifyCommand))]
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
        _status = _verifiedId is null ? "Требуется проверка" : "Доступ подтверждён";
        _statusSeverity = _verifiedId is null ? StatusSeverity.Warning : StatusSeverity.Success;
    }

    public PlatformAuthRole Role { get; }

    public string Title => Role == PlatformAuthRole.Bot ? "Бот" : "Стример";

    public string AuthorizationLabel => Role == PlatformAuthRole.Bot ? "Войти как бот" : "Войти как стример";

    public string AuthorizationAutomationId => Role == PlatformAuthRole.Bot ? "ChannelBotAuthorization" : "ChannelBroadcasterAuthorization";

    public string ContextId { get; private set; }

    public string? AccountId { get; private set; }

    public bool CanAuthorize { get; private set; }

    public bool CanManageAccount => CanAuthorize || _supportsAccounts;

    public bool CanSelectAccount => _canVerify && !IsBusy && Accounts.Count > 0;

    public bool NeedsVerification => AccountId is not null && _verifiedId != AccountId;

    internal bool IsEdited => _saved != (AccountId, ContextId, _verifiedId);

    internal void AcceptEdits() => _saved = (AccountId, ContextId, _verifiedId);

    public ChannelAccountOption? SelectedAccount
    {
        get => Accounts.FirstOrDefault(option => string.Equals(option.AccountId, AccountId, StringComparison.Ordinal));
        set
        {
            if (value is not null)
            {
                Assign(value.AccountId, value.ContextId);
            }
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
            SetStatus(string.Empty, StatusSeverity.None);
            NotifyAccountOptionsChanged();
            return;
        }

        var options = new List<ChannelAccountOption>();
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

                var label = grant.HasToken ? account.DisplayName : $"{account.DisplayName} · нужен вход";
                options.Add(new(account.Key.AccountId, label, grant.ContextId));
            }
        }

        var selected = options.FirstOrDefault(option => option.AccountId == AccountId);
        if (AccountId is { } assigned && (selected is null || selected.ContextId != ContextId))
        {
            SetStatus(ReauthorizationNotice, StatusSeverity.Warning);
            if (selected is null)
            {
                options.Add(new(assigned, $"{assigned} · нужен вход", ContextId));
            }
        }
        else if (selected is null)
        {
            SetStatus(Role == PlatformAuthRole.Bot
                ? "Войдите как бот или выберите аккаунт бота и проверьте доступ."
                : "При необходимости выберите аккаунт стримера и проверьте доступ.",
                Role == PlatformAuthRole.Bot ? StatusSeverity.Warning : StatusSeverity.None);
        }
        else if (!hasAssignedAuthorization)
        {
            SetStatus(ReauthorizationNotice, StatusSeverity.Warning);
        }
        else if (Status == ReauthorizationNotice)
        {
            SetStatus(NeedsVerification ? "Требуется проверка" : "Доступ подтверждён",
                NeedsVerification ? StatusSeverity.Warning : StatusSeverity.Success);
        }

        Accounts = options;
        NotifyAccountOptionsChanged();
    }

    private void NotifyAccountOptionsChanged()
    {
        OnPropertyChanged(nameof(CanAuthorize));
        OnPropertyChanged(nameof(CanManageAccount));
        OnPropertyChanged(nameof(CanSelectAccount));
        OnPropertyChanged(nameof(SelectedAccount));
        AuthorizeCommand.NotifyCanExecuteChanged();
        VerifyCommand.NotifyCanExecuteChanged();
    }

    public void Assign(string accountId, string contextId)
    {
        if (AccountId == accountId && ContextId == contextId)
        {
            return;
        }

        AccountId = accountId;
        ContextId = contextId;
        _verifiedId = null;
        SetStatus("Требуется проверка", StatusSeverity.Warning);
        OnPropertyChanged(nameof(SelectedAccount));
        VerifyCommand.NotifyCanExecuteChanged();
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
        if (Role == PlatformAuthRole.Bot)
        {
            entry.BotAccountId = AccountId;
            entry.BotContextId = ContextId;
            entry.BotVerifiedSubjectId = _verifiedId;
        }
        else
        {
            entry.BroadcasterAccountId = AccountId;
            entry.BroadcasterContextId = ContextId;
            entry.BroadcasterVerifiedSubjectId = _verifiedId;
        }
    }

    private bool CanAuthorizeNow() => CanAuthorize && !IsBusy;

    private bool CanVerifyNow() => _canVerify && !IsBusy && !string.IsNullOrWhiteSpace(AccountId);

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
