using KeepShell.Services;
using KeepShell.Services.Modal;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class SettingsWriteGateNoticeTests
{
    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-write-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private string _root = null!;

    [Test]
    public async Task Закрытый_гейт_записи_не_выдаётся_за_сохранённые_названия_баллов()
    {
        var settingsPath = Path.Combine(_root, "settings.json");
        var settingsManager = await CreateBlockedSettingsManagerAsync(settingsPath);

        var dialogs = new FakeDialogService(dialog => ((PointTermDialogViewModel)dialog).Singular = "очко");
        var page = CreateUserStatisticsPage(settingsManager, dialogs);

        await page.EditPointTermCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(settingsPath), Is.False, "Гейт не пустил запись на диск");
            Assert.That(settingsManager.Current.Ranks.PointTerm.Singular, Is.EqualTo("очко"),
                "В памяти название действует до перезапуска");
            Assert.That(dialogs.Warnings, Has.Count.EqualTo(1));
            Assert.That(dialogs.Warnings[0], Does.Contain("не записаны").And.Contain("Перезапустите"));
            Assert.That(dialogs.Infos, Is.Empty, "Незаписанный файл – это не сообщение об успехе");
        });
    }

    [Test]
    public async Task Закрытый_гейт_записи_останавливает_шаг_подключения_мастера()
    {
        var settingsPath = Path.Combine(_root, "settings.json");
        var settingsManager = await CreateBlockedSettingsManagerAsync(settingsPath);

        var accountsStore = new AccountsStore(null, Path.Combine(_root, "accounts.json"));
        var connection = new FakeBotConnectionController();

        using var page = new BotConnectionPageViewModel(
            connection,
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
            settingsManager,
            accountsStore,
            NullLogger<BotConnectionPageViewModel>.Instance);

        page.OnEnter(new(new AppSettings(), new TwitchAccountSettings(), new TwitchAccountSettings()));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(settingsPath), Is.False, "Гейт не пустил запись на диск");
            Assert.That(page.CanAdvance, Is.False, "Шаг остановлен, дальше мастер не пускает");
            Assert.That(page.ShowRetry, Is.False, "Повтор здесь не помогает – помогает перезапуск");
            Assert.That(page.DetailsText, Does.Contain("не записаны").And.Contain("заново"));
            Assert.That(connection.StartCount, Is.Zero, "Подключение не начинается поверх незаписанных настроек");
        });
    }

    private static async Task<SettingsManager> CreateBlockedSettingsManagerAsync(string settingsPath)
    {
        var gate = new SettingsWriteGate();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath, gate);

        await gate.RunExternalWriteAsync(() => Task.FromResult(0), _ => new[] { "settings.json" });

        return settingsManager;
    }

    private static UserStatisticsPageViewModel CreateUserStatisticsPage(SettingsManager settingsManager, IDialogService dialogs)
    {
        var statistics = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        return new(
            statistics,
            null!,
            new UserRankService(settingsManager),
            new UserPointsManagementService(statistics, settingsManager, eventBus),
            new FakeChannelProvider(),
            settingsManager,
            dialogs,
            eventBus);
    }

    private sealed class FakeChannelProvider : IChannelProvider
    {
        public string? Channel => "test";
    }

    private sealed class FakeBotConnectionController : IBotConnectionController
    {
        public int StartCount { get; private set; }

        public bool IsBusy => false;

        public BotLifecyclePhase CurrentPhase => BotLifecyclePhase.Idle;

        public void StartConnection() => StartCount++;

        public void CancelConnection()
        {
        }

        public Task WaitForConnectionAsync() => Task.CompletedTask;

        public Task StopAsync(BotStopMode mode) => Task.CompletedTask;
    }

    private sealed class FakeDialogService(Action<IDialogViewModel> edit) : IDialogService
    {
        public List<string> Warnings { get; } = [];

        public List<string> Infos { get; } = [];

        public Task<bool> ShowAsync(IDialogViewModel viewModel)
        {
            edit(viewModel);
            return Task.FromResult(true);
        }

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => ShowAsync(viewModel);

        public bool Confirm(string title, string message, bool defaultYes = false) => true;

        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;

        public void Info(string title, string message) => Infos.Add(message);

        public void Warning(string title, string message) => Warnings.Add(message);

        public void Error(string title, string message) => throw new InvalidOperationException(message);
    }
}
