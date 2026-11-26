using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class YouTubeOnboardingPageViewModel : OnboardingPageViewModelBase, IDisposable
{
    private OnboardingContext? _context;

    [ObservableProperty]
    private string _validationHint = string.Empty;

    public YouTubeOnboardingPageViewModel(YouTubeSettingsSectionViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        Editor = editor;
        Editor.ShowQuotaDiagnostics = false;
        Editor.ShowEnableToggle = false;
        Editor.PropertyChanged += OnEditorPropertyChanged;
        Editor.SettingChanged += OnEditorSettingChanged;
        Editor.Bot.PropertyChanged += OnEditorPropertyChanged;
    }

    public override string PageTitle => "Настройка YouTube";

    public YouTubeSettingsSectionViewModel Editor { get; }

    public bool HasValidationHint => ValidationHint.Length > 0;

    public override bool IsApplicable(OnboardingContext context) => context.YouTubeSettings.Enabled;

    public override void OnEnter(OnboardingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        Editor.Load(
            context.YouTubeSettings,
            context.YouTubeClientSecret,
            context.YouTubeBotAccount,
            context.YouTubeBroadcasterAccount);
        UpdateValidation();
    }

    public override Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        UpdateValidation();
        if (!CanAdvance)
        {
            return Task.FromResult(false);
        }

        Editor.SaveTo(
            context.YouTubeSettings,
            out var secret,
            context.YouTubeBotAccount,
            context.YouTubeBroadcasterAccount);
        context.YouTubeClientSecret = secret;
        return Task.FromResult(true);
    }

    public void Dispose()
    {
        Editor.PropertyChanged -= OnEditorPropertyChanged;
        Editor.SettingChanged -= OnEditorSettingChanged;
        Editor.Bot.PropertyChanged -= OnEditorPropertyChanged;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateValidation();
    }

    private void OnEditorSettingChanged(object? sender, EventArgs e)
    {
        UpdateValidation();
        if (_context is not null)
        {
            Editor.SaveTo(
                _context.YouTubeSettings,
                out var secret,
                _context.YouTubeBotAccount,
                _context.YouTubeBroadcasterAccount);
            _context.YouTubeClientSecret = secret;
        }
    }

    private void UpdateValidation()
    {
        if (!Editor.Enabled)
        {
            ValidationHint = string.Empty;
            CanAdvance = true;
            OnPropertyChanged(nameof(HasValidationHint));
            return;
        }

        if (string.IsNullOrWhiteSpace(Editor.ClientId))
        {
            ValidationHint = "Укажите Client ID проекта Google Cloud для YouTube.";
            CanAdvance = false;
        }
        else if (string.IsNullOrWhiteSpace(Editor.Channel))
        {
            ValidationHint = "Укажите @handle, ID (UC…) или ссылку на канал YouTube.";
            CanAdvance = false;
        }
        else if (Editor.HasQuotaBudgetError)
        {
            ValidationHint = Editor.QuotaBudgetError;
            CanAdvance = false;
        }
        else if (!PlatformAuthRoles.HasCredentials(Editor.Bot.AccessTokenValue, Editor.Bot.RefreshTokenValue))
        {
            ValidationHint = "Авторизуйте аккаунт бота YouTube для работы в чате.";
            CanAdvance = false;
        }
        else
        {
            ValidationHint = string.Empty;
            CanAdvance = true;
        }

        OnPropertyChanged(nameof(HasValidationHint));
    }
}
