using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Tests.Settings.Stores;

[TestFixture]
public sealed class SettingsWriteFactTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("settings-write-fact");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(true);
    }

    public static IEnumerable<TestCaseData> Writers()
    {
        yield return Case("accounts.json", "AccountsStore.SaveAll", path =>
            new AccountsStore(null, path).SaveAll(new() { Login = "bot" }, new() { Login = "streamer" }));

        yield return Case("chat.json", "ChatSettingsStore.Save", path =>
            new ChatSettingsStore(null, null, path).Save(new() { ShowHistoryInApp = true }));

        yield return Case("chat.json", "ChatSettingsStore.Mutate", path =>
            new ChatSettingsStore(null, null, path).Mutate(settings => settings.ShowHistoryInObs = true));

        yield return Case("youtube.json", "YouTubeSettingsStore.Save", path =>
            new YouTubeSettingsStore(null, path).Save(new() { Enabled = true }));

        yield return Case("youtube.json", "YouTubeSettingsStore.Mutate", path =>
            new YouTubeSettingsStore(null, path).Mutate(settings => settings.Enabled = true));

        yield return Case("youtube-accounts.json", "YouTubeAccountsStore.SaveAll", path =>
            new YouTubeAccountsStore(null, path).SaveAll("sec", new() { ChannelId = "bot" }, new() { ChannelId = "caster" }));

        yield return Case("youtube-accounts.json", "YouTubeAccountsStore.SaveClientSecret", path =>
            new YouTubeAccountsStore(null, path).SaveClientSecret("sec"));

        yield return Case("broadcast-profiles.json", "BroadcastProfilesStore.Save", path =>
            new BroadcastProfilesStore(null, path).Save(new()));

        yield return Case("broadcast-profiles.json", "BroadcastProfilesStore.Mutate", path =>
            new BroadcastProfilesStore(null, path).Mutate(settings => settings.LastAppliedProfileId = Guid.NewGuid()));

        yield return Case("broadcast-profiles.json", "BroadcastProfilesManager.Upsert", path =>
            CreateProfilesManager(path).Upsert(new() { Name = "Стрим" }));

        yield return Case("broadcast-profiles.json", "BroadcastProfilesManager.Remove", path =>
            CreateProfilesManager(path).Remove(Guid.NewGuid()));

        yield return Case("polls.json", "PollsStore.Save", path =>
            new PollsStore(null, path).Save(new()));

        yield return Case("polls.json", "PollsStore.Mutate", path =>
            new PollsStore(null, path).Mutate(settings => settings.HistoryMaxItems = 42));

        yield return Case("polls.json", "PollProfilesManager.Upsert", path =>
            CreatePollsManager(path).Upsert(CreatePoll()));

        yield return Case("polls.json", "PollProfilesManager.ReplaceAll", path =>
            CreatePollsManager(path).ReplaceAll([CreatePoll()]));

        yield return Case("polls.json", "PollProfilesManager.Remove", path =>
            CreatePollsManager(path).Remove(Guid.NewGuid()));

        yield return Case("channels.json", "ChannelCatalogStore.Save", path =>
            new ChannelCatalogStore(null, path).Save(new()));

        yield return Case("channels.json", "ChannelCatalogStore.Mutate", path =>
            new ChannelCatalogStore(null, path).Mutate(settings => settings.Groups.Add(new() { Name = "Основные" })));
    }

    [TestCaseSource(nameof(Writers))]
    public void Обёртка_отдаёт_наружу_факт_записи(string fileName, Func<string, bool> write)
    {
        var openPath = Path.Combine(_directory.FullName, "open", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(openPath)!);

        var written = write(openPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(written, Is.True);
            Assert.That(File.Exists(openPath), Is.True);
        }
    }

    private static TestCaseData Case(string fileName, string name, Func<string, bool> write)
    {
        return new TestCaseData(fileName, write).SetArgDisplayNames(name);
    }

    private static BroadcastProfilesManager CreateProfilesManager(string path)
    {
        return new(new(null, path),
            Substitute.For<IChannelInformationApplier>(),
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
            TimeProvider.System,
            NullLogger<BroadcastProfilesManager>.Instance);
    }

    private static PollProfilesManager CreatePollsManager(string path)
    {
        return new(new(null, path),
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
            NullLogger<PollProfilesManager>.Instance);
    }

    private static PollProfile CreatePoll()
    {
        return new()
        {
            Name = "Опрос",
            Title = "Что дальше?",
            Choices = ["Да", "Нет"],
        };
    }
}
