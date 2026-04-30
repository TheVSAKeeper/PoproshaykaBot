using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tests.Broadcast.Profiles;

[TestFixture]
public class GameCategoryResolverTests
{
    [SetUp]
    public void SetUp()
    {
        _searchApi = Substitute.For<ITwitchSearchApi>();
        _settings = new();
        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance,
            Substitute.For<IEventBus>());

        _settingsManager.Current.Returns(_settings);
        _resolver = new(_searchApi, _settingsManager,
            NullLogger<GameCategoryResolver>.Instance);
    }

    private ITwitchSearchApi _searchApi = null!;
    private SettingsManager _settingsManager = null!;
    private AppSettings _settings = null!;
    private GameCategoryResolver _resolver = null!;

    [Test]
    public async Task SearchAsync_ReturnsApiResults()
    {
        _searchApi.SearchCategoriesAsync("dota", 10, Arg.Any<CancellationToken>())
            .Returns([
                new("1", "Dota 2", ""),
                new("2", "DOTA", ""),
            ]);

        var results = await _resolver.SearchAsync("dota", CancellationToken.None);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Id, Is.EqualTo("1"));
    }

    [Test]
    public async Task ResolveAsync_ReturnsFirstResult()
    {
        _searchApi.SearchCategoriesAsync("dota", 10, Arg.Any<CancellationToken>())
            .Returns([new("1", "Dota 2", "")]);

        var result = await _resolver.ResolveAsync("dota", CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("1"));
    }

    [Test]
    public async Task ResolveAsync_NoResults_ReturnsNull()
    {
        _searchApi.SearchCategoriesAsync("xxx", 10, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _resolver.ResolveAsync("xxx", CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RememberAsync_AddsToCache_AndLimitsTo20()
    {
        for (var i = 0; i < 25; i++)
        {
            await _resolver.RememberAsync(new(i.ToString(), $"Game {i}", ""));
        }

        Assert.That(_settings.Twitch.Infrastructure.RecentCategories, Has.Count.EqualTo(20));
        Assert.That(_settings.Twitch.Infrastructure.RecentCategories[0].Id, Is.EqualTo("24"));
    }

    [Test]
    public async Task RememberAsync_ExistingId_MovesToTop()
    {
        await _resolver.RememberAsync(new("A", "Game A", ""));
        await _resolver.RememberAsync(new("B", "Game B", ""));
        await _resolver.RememberAsync(new("A", "Game A", ""));

        var recents = _settings.Twitch.Infrastructure.RecentCategories;
        Assert.That(recents, Has.Count.EqualTo(2));
        Assert.That(recents[0].Id, Is.EqualTo("A"));
    }
}
