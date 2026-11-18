using PoproshaykaBot.Wpf.Views.Tiles;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
public sealed class ChatBrowserNavigationTests
{
    private static readonly Uri FirstChat = new("https://www.youtube.com/live_chat?v=first");
    private static readonly Uri SecondChat = new("https://www.youtube.com/live_chat?v=second");

    [Test]
    public void Ошибка_и_сбой_рендера_требуют_повторной_загрузки_того_же_чата()
    {
        var navigation = new ChatBrowserNavigation();
        navigation.Begin(FirstChat);
        navigation.Started(FirstChat.AbsoluteUri, 1, false);
        Assert.That(navigation.TryComplete(1, success: false), Is.True);
        Assert.That(navigation.CanReuse(FirstChat), Is.False);

        navigation.Begin(FirstChat);
        navigation.Started(FirstChat.AbsoluteUri, 2, false);
        Assert.That(navigation.TryComplete(2, success: true), Is.True);
        Assert.That(navigation.CanReuse(FirstChat), Is.True);

        navigation.RequireRetry();
        Assert.That(navigation.CanReuse(FirstChat), Is.False);
    }

    [Test]
    public void Поздняя_ошибка_предыдущего_эфира_не_принадлежит_новому_запросу()
    {
        var navigation = new ChatBrowserNavigation();
        navigation.Begin(FirstChat);
        navigation.Started(FirstChat.AbsoluteUri, 1, false);
        navigation.Begin(SecondChat);
        navigation.Started(FirstChat.AbsoluteUri, 1, true);
        Assert.That(navigation.TryComplete(1, success: false), Is.False);

        navigation.Started(SecondChat.AbsoluteUri, 2, false);
        Assert.That(navigation.TryComplete(1, success: false), Is.False);
        Assert.That(navigation.TryComplete(2, success: true), Is.True);
    }

    [Test]
    public void Возврат_того_же_эфира_после_офлайна_не_принимает_пустую_страницу()
    {
        var navigation = new ChatBrowserNavigation();
        navigation.Begin(FirstChat);
        navigation.Begin(null);
        Assert.That(navigation.CanReuse(FirstChat), Is.False);

        navigation.Begin(FirstChat);
        navigation.Started(FirstChat.AbsoluteUri, 3, false);
        navigation.Started("about:blank", 2, false);
        Assert.That(navigation.TryComplete(2, success: true), Is.False);
        Assert.That(navigation.TryComplete(3, success: true), Is.True);
    }
}
