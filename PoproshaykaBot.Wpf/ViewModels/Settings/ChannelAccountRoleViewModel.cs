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
    private readonly Action _onEdited;
    private readonly Func<ChannelAccountRoleViewModel, Task> _verify;
    private readonly Func<ChannelAccountRoleViewModel, Task> _authorize;
    private string? _verifiedId;

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
        _status = _verifiedId is null ? "Требуется проверка" : "Доступ подтверждён";
        _statusSeverity = _verifiedId is null ? StatusSeverity.Warning : StatusSeverity.Success;
    }

    public PlatformAuthRole Role { get; }

    public string Title => Role == PlatformAuthRole.Bot ? "Бот" : "Стример";

    public string ContextId { get; private set; }

    public string? AccountId { get; private set; }

    public bool CanAuthorize { get; private set; }

    public bool CanSelectAccount => !IsBusy;

    public bool NeedsVerification => AccountId is not null && _verifiedId != AccountId;

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

    public void RefreshAccounts(IReadOnlyList<PlatformAccount> accounts, bool canAuthorize)
    {
        var options = new List<ChannelAccountOption>();
        foreach (var account in accounts)
        {
            var grants = account.Authorizations.Where(grant => grant.Role == Role).ToArray();
            var current = grants.FirstOrDefault(grant => grant.ContextId == ContextId);
            var grant = account.Key.AccountId == AccountId && current is not null
                ? current
                : grants.FirstOrDefault(value => value.HasToken) ?? grants.FirstOrDefault();
            if (grant is not null)
            {
                var label = grant.HasToken ? account.DisplayName : $"{account.DisplayName} · нужен вход";
                options.Add(new(account.Key.AccountId, label, grant.ContextId));
            }
        }

        var selected = options.FirstOrDefault(option => option.AccountId == AccountId);
        if (AccountId is { } assigned && (selected is null || selected.ContextId != ContextId))
        {
            _verifiedId = null;
            SetStatus("Подключите назначенный аккаунт заново", StatusSeverity.Warning);
            if (selected is null)
            {
                options.Add(new(assigned, $"{assigned} · нужен вход", ContextId));
            }
        }
        else if (selected is null)
        {
            SetStatus("Аккаунт не выбран", StatusSeverity.None);
        }

        CanAuthorize = canAuthorize;
        Accounts = options;
        OnPropertyChanged(nameof(SelectedAccount));
        AuthorizeCommand.NotifyCanExecuteChanged();
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

    private bool CanVerifyNow() => !IsBusy && !string.IsNullOrWhiteSpace(AccountId);

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
