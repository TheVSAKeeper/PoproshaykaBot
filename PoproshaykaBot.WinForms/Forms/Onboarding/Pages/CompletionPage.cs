using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Onboarding;
using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

public sealed partial class CompletionPage : OnboardingPageBase
{
    public CompletionPage()
    {
        InitializeComponent();
    }

    [Inject]
    public SettingsManager SettingsManager { get; internal init; } = null!;

    [Inject]
    public AccountsStore AccountsStore { get; internal init; } = null!;

    [Inject]
    public IEventBus EventBus { get; internal init; } = null!;

    [Inject]
    public KestrelHttpServer KestrelHttpServer { get; internal init; } = null!;

    [Inject]
    public IOnboardingChannelValidator ChannelValidator { get; internal init; } = null!;

    [Inject]
    public BotConnectionManager BotConnectionManager { get; internal init; } = null!;

    [Inject]
    public ILogger<CompletionPage> Logger { get; internal init; } = null!;

    public override string PageTitle => "Готово";

    public override void OnEnter(OnboardingContext context)
    {
        var botLogin = string.IsNullOrWhiteSpace(context.BotAccount.Login) ? "—" : context.BotAccount.Login;
        var broadcasterLogin = string.IsNullOrWhiteSpace(context.BroadcasterAccount.Login)
            ? "—"
            : context.BroadcasterAccount.Login;

        _summaryLabel.Text =
            $"Канал: {context.Settings.Twitch.Channel}"
            + Environment.NewLine
            + $"Аккаунт бота: @{botLogin}"
            + Environment.NewLine
            + $"Аккаунт стримера: @{broadcasterLogin}";

        _warningLabel.Text = string.Empty;
        _warningLabel.Visible = false;

        SetCanAdvance(true);
    }

    public override async Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        if (!await ConfirmChannelAsync(context))
        {
            return false;
        }

        var oldPort = SettingsManager.Current.Twitch.HttpServerPort;
        var newPort = context.Settings.Twitch.HttpServerPort;
        var portChanged = oldPort != newPort;

        try
        {
            SettingsManager.SaveSettings(context.Settings);
            AccountsStore.SaveAll(context.BotAccount, context.BroadcasterAccount);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Не удалось сохранить настройки в onboarding-мастере");
            MessageBox.Show(this,
                $"Не удалось сохранить настройки: {exception.Message}",
                "Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return false;
        }

        await EventBus.PublishAsync(new TwitchAuthorizationRefreshed(TwitchOAuthRole.Bot));
        await EventBus.PublishAsync(new TwitchAuthorizationRefreshed(TwitchOAuthRole.Broadcaster));

        if (portChanged)
        {
            await TryRestartHttpServerAsync(newPort);
        }

        if (_autoConnectCheckBox.Checked)
        {
            try
            {
                BotConnectionManager.StartConnection();
            }
            catch (InvalidOperationException exception)
            {
                Logger.LogWarning(exception, "Авто-подключение бота отклонено");
            }
        }

        return true;
    }

    private async Task<bool> ConfirmChannelAsync(OnboardingContext context)
    {
        var channel = context.Settings.Twitch.Channel;
        if (string.IsNullOrWhiteSpace(channel))
        {
            return true;
        }

        _warningLabel.Text = "Проверка канала на Twitch...";
        _warningLabel.ForeColor = Color.Blue;
        _warningLabel.Visible = true;

        ChannelValidationResult result;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            result = await ChannelValidator.ValidateAsync(channel,
                context.Settings.Twitch.ClientId,
                context.BotAccount.AccessToken,
                cts.Token);
        }
        catch (Exception exception)
        {
            Logger.LogDebug(exception, "Сбой проверки канала {Channel}", channel);
            result = ChannelValidationResult.Skipped;
        }

        _warningLabel.Visible = false;

        if (result != ChannelValidationResult.NotFound)
        {
            return true;
        }

        _warningLabel.Text = $"Канал «{channel}» не найден на Twitch.";
        _warningLabel.ForeColor = Color.DarkOrange;
        _warningLabel.Visible = true;

        var answer = MessageBox.Show(this,
            $"""
             Канал «{channel}» не найден на Twitch. Возможно, имя написано с ошибкой.

             Всё равно сохранить настройки?
             """,
            "Канал не найден",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        return answer == DialogResult.Yes;
    }

    private async Task TryRestartHttpServerAsync(int newPort)
    {
        try
        {
            if (KestrelHttpServer.IsRunning)
            {
                await KestrelHttpServer.StopAsync();
            }

            await KestrelHttpServer.StartAsync();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Не удалось перезапустить HTTP сервер на порту {Port}", newPort);
            MessageBox.Show(this,
                $"""
                 HTTP сервер не удалось перезапустить на порту {newPort}.
                 Настройки сохранены — потребуется перезапуск приложения.
                 """,
                "Внимание",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
