using System.Text.RegularExpressions;
using PoproshaykaBot.Core.Tests.YouTube.Fixtures;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
internal sealed partial class InnerTubeFixturesSanitizationTests
{
    private static readonly string[] ExpectedFixtures =
    [
        "errors_429_403_consent.json",
        "initial_mixed_with_ticker.json",
        "reload_and_ended.json",
        "rich_events_superchat_membership_mod.json",
        "top_vs_live_window_comparison.json",
        "watch_live_chat_selector.html",
    ];

    private static readonly string[] ForbiddenTokens =
    [
        "set-cookie",
        "cookie:",
        "authorization:",
        "x-goog-visitor-id",
        "visitordata",
        "sapisid",
        "__secure-3psid",
        "bearer ",
    ];

    [Test]
    public void All_Six_InnerTube_Fixtures_Are_Embedded_And_Sanitized()
    {
        var resourceNames = InnerTubeFixtureLoader.GetAllFixtureResourceNames();
        Assert.That(resourceNames, Has.Count.EqualTo(ExpectedFixtures.Length));

        foreach (var fileName in ExpectedFixtures)
        {
            var content = InnerTubeFixtureLoader.ReadText(fileName);
            Assert.That(content, Is.Not.Empty, fileName);

            foreach (var token in ForbiddenTokens)
            {
                Assert.That(
                    content.Contains(token, StringComparison.OrdinalIgnoreCase),
                    Is.False,
                    $"Фикстура {fileName} содержит запрещённый токен '{token}'.");
            }

            Assert.That(
                PrivateIpv4Regex().IsMatch(content),
                Is.False,
                $"Фикстура {fileName} содержит внутренний IPv4-адрес.");
        }
    }

    [GeneratedRegex(
        @"\b(?:127\.0\.0\.1|10\.\d{1,3}\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex PrivateIpv4Regex();
}
