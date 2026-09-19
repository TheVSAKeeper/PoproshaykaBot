using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class StreamHistoryPageTests
{
    private static readonly DateTimeOffset Start = new(new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Local));

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-streams-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void Фильтр_по_игре_пересчитывает_список_сводку_и_тренд()
    {
        var page = Create(new MemorySettings(), Session(0, "Just Chatting"), Session(1, "Minecraft"), Session(2, "Just Chatting"));

        page.TrySelectAt(0);
        page.FilterByGameCommand.Execute(page.Segments[0]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Sessions, Has.Count.EqualTo(2));
            Assert.That(page.HasGameFilter, Is.True);
            Assert.That(page.GameFilterText, Is.EqualTo("Игра: Just Chatting"));
            Assert.That(page.TotalSessionsText, Is.EqualTo("2"));
            Assert.That(page.Trend, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Чип_снимает_фильтр_и_возвращает_все_стримы()
    {
        var page = Create(new MemorySettings(), Session(0, "Just Chatting"), Session(1, "Minecraft"));

        page.TrySelectAt(0);
        page.FilterByGameCommand.Execute(page.Segments[0]);
        page.ClearGameFilterCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Sessions, Has.Count.EqualTo(2));
            Assert.That(page.HasGameFilter, Is.False);
        }
    }

    [Test]
    public void Повторный_выбор_той_же_игры_снимает_фильтр()
    {
        var page = Create(new MemorySettings(), Session(0, "Just Chatting"), Session(1, "Minecraft"));

        page.TrySelectAt(0);
        page.FilterByGameCommand.Execute(page.Segments[0]);
        page.FilterByGameCommand.Execute(page.Segments[0]);

        Assert.That(page.HasGameFilter, Is.False);
    }

    [Test]
    public void Сегмент_без_категории_фильтра_не_ставит()
    {
        var page = Create(new MemorySettings(), Session(0, null));

        page.TrySelectAt(0);
        page.FilterByGameCommand.Execute(page.Segments[0]);

        Assert.That(page.HasGameFilter, Is.False);
    }

    [TestCase(20, 20)]
    [TestCase(40, 30)]
    public void Глубина_ограничивает_число_столбиков(int length, int expected)
    {
        var page = Create(new MemorySettings(), Enumerable.Range(0, 30).Select(index => Session(index, "Just Chatting")).ToArray());

        page.LengthOptions[Array.IndexOf(StreamHistoryPageViewModel.TrendLengths, length)].Command.Execute(null);

        Assert.That(page.Trend, Has.Count.EqualTo(expected));
    }

    [Test]
    public void Метрика_и_глубина_переживают_перезапуск()
    {
        var settings = new MemorySettings();
        var page = Create(settings, Session(0, "Just Chatting"), Session(1, "Minecraft"));

        page.MetricOptions[Array.IndexOf(StreamHistoryPageViewModel.TrendMetrics, StreamTrendMetric.Messages)].Command.Execute(null);
        page.LengthOptions[Array.IndexOf(StreamHistoryPageViewModel.TrendLengths, 20)].Command.Execute(null);

        var restarted = Create(settings, Session(0, "Just Chatting"), Session(1, "Minecraft"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restarted.TrendCaption, Does.StartWith("Сообщения"));
            Assert.That(restarted.MetricOptions[2].IsChecked, Is.True);
            Assert.That(restarted.LengthOptions[0].IsChecked, Is.True);
        }
    }

    [Test]
    public void Столбик_считает_выбранную_метрику_и_подписывает_максимум()
    {
        var page = Create(new MemorySettings(), Session(0, "Just Chatting", messages: 10), Session(1, "Minecraft", messages: 40));

        page.MetricOptions[Array.IndexOf(StreamHistoryPageViewModel.TrendMetrics, StreamTrendMetric.Messages)].Command.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Trend[0].Value, Is.EqualTo(10));
            Assert.That(page.Trend[1].Value, Is.EqualTo(40));
            Assert.That(page.Trend[0].IsPeak, Is.False);
            Assert.That(page.Trend[1].IsPeak, Is.True);
            Assert.That(page.Trend[1].Label, Does.Contain("сообщений 40"));
        }
    }

    [Test]
    public void Скрытая_полоса_уходит_но_меню_остаётся_на_месте()
    {
        ISettingsStore settings = new MemorySettings();
        var page = Create(settings, Session(0, "Just Chatting"), Session(1, "Minecraft"));

        page.ToggleTrendCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.HasTrend, Is.False);
            Assert.That(page.CanShowTrend, Is.True);
            Assert.That(page.TrendCaption, Does.Contain("скрыта"));
            Assert.That(settings.GetBool(SettingsKeys.StreamTrendVisible, true), Is.False);
        }
    }

    [Test]
    public void Топ_категорий_считается_по_отфильтрованному_набору()
    {
        var page = Create(new MemorySettings(), Session(0, "Just Chatting"), Session(1, "Minecraft"), Session(2, "Just Chatting"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TopCategories.Select(category => category.Game), Is.EqualTo(new[] { "Just Chatting", "Minecraft" }));
            Assert.That(page.TopCategories[0].SessionsText, Is.EqualTo("2 стрима"));
            Assert.That(page.HasSummaryStrip, Is.True);
        }

        page.FilterByGameCommand.Execute(page.TopCategories[1]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.GameFilter, Is.EqualTo("Minecraft"));
            Assert.That(page.TopCategories.Select(category => category.Game), Is.EqualTo(new[] { "Minecraft" }));
            Assert.That(page.TopCategories[0].ShareText, Is.EqualTo("100 %"));
            Assert.That(page.Records, Is.Empty);
        }
    }

    [Test]
    public void Повторный_клик_по_той_же_категории_снимает_фильтр()
    {
        var page = Create(new MemorySettings(), Session(0, "Just Chatting"), Session(1, "Minecraft"));

        page.FilterByGameCommand.Execute(page.TopCategories[0]);
        page.FilterByGameCommand.Execute(page.TopCategories[0]);

        Assert.That(page.HasGameFilter, Is.False);
    }

    [Test]
    public void Карточка_рекорда_выбирает_свою_сессию()
    {
        var page = Create(
            new MemorySettings(),
            Session(0, "Just Chatting", messages: 10),
            Session(1, "Minecraft", messages: 900));

        var record = page.Records.Single(card => card.Kind == StreamRecordKind.Messages);

        page.SelectSessionCommand.Execute(record.Row);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.CanSelect, Is.True);
            Assert.That(record.ValueText, Is.EqualTo("900"));
            Assert.That(page.SelectedRow?.MessageCount, Is.EqualTo(900));
        }
    }

    [Test]
    public void Свёрнутость_сводки_переживает_перезапуск()
    {
        ISettingsStore settings = new MemorySettings();
        var page = Create(settings, Session(0, "Just Chatting"), Session(1, "Minecraft"));

        Assert.That(page.IsSummaryExpanded, Is.False);

        page.ToggleSummaryCommand.Execute(null);

        var restarted = Create(settings, Session(0, "Just Chatting"), Session(1, "Minecraft"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.GetBool(SettingsKeys.StreamSummaryExpanded, false), Is.True);
            Assert.That(restarted.IsSummaryExpanded, Is.True);
            Assert.That(restarted.IsSummaryOpen, Is.True);
            Assert.That(restarted.HasSummaryStrip, Is.True);
        }

        restarted.ToggleSummaryCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restarted.IsSummaryOpen, Is.False);
            Assert.That(Create(settings, Session(0, "Just Chatting")).IsSummaryExpanded, Is.False);
        }
    }

    [Test]
    public void Клик_по_известному_чаттеру_просит_открыть_пользователя()
    {
        var page = Create(new MemorySettings(), Session(0, "Just Chatting", chatterId: "42"));
        var requested = new List<string>();

        page.UserRequested += (_, userId) => requested.Add(userId);
        page.TrySelectAt(0);
        page.OpenChatterCommand.Execute(page.Chatters[0]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Chatters[0].CanOpen, Is.True);
            Assert.That(requested, Is.EqualTo(new[] { "42" }));
        }
    }

    [Test]
    public void Чаттера_которого_нет_в_статистике_страница_не_открывает()
    {
        var page = Create(new MemorySettings(), Session(0, "Just Chatting", chatterId: "777"));
        var requested = new List<string>();

        page.UserRequested += (_, userId) => requested.Add(userId);
        page.TrySelectAt(0);
        page.OpenChatterCommand.Execute(page.Chatters[0]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Chatters[0].CanOpen, Is.False);
            Assert.That(page.Chatters[0].Hint, Does.Contain("нет"));
            Assert.That(requested, Is.Empty);
        }
    }

    private static StreamSessionRecord Session(int index, string? game, long messages = 100, string chatterId = "42")
    {
        var started = Start.AddDays(index);

        return new()
        {
            Channel = "bobito217",
            StartedAt = started,
            EndedAt = started.AddHours(3),
            Title = "эфир",
            Game = game,
            MessageCount = messages,
            ChatterCount = 1,
            PeakViewers = 10 + index,
            AverageViewers = 5,
            Chatters =
            [
                new() { UserId = chatterId, DisplayName = "qp_illson", MessageCount = messages },
            ],
            Segments =
            [
                new()
                {
                    StartedAt = started,
                    EndedAt = started.AddHours(3),
                    Title = "эфир",
                    Game = game,
                    MessageCount = messages,
                    PeakViewers = 10 + index,
                    AverageViewers = 5,
                },
            ],
        };
    }

    private StreamHistoryPageViewModel Create(ISettingsStore settings, params StreamSessionRecord[] sessions)
    {
        var store = new StreamSessionHistoryStore(filePath: Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json"));

        foreach (var session in sessions)
        {
            store.Append(session);
        }

        var users = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        users.ReplaceAll([new() { UserId = "42", Name = "qp_illson" }]);

        var boxArt = new GameBoxArtProvider(new FakeBoxArtCache(), NullLogger<GameBoxArtProvider>.Instance);

        return new(store, users, settings, boxArt, new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance));
    }
}
