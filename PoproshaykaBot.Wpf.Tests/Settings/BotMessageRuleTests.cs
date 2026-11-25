using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public sealed class BotMessageRuleTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("poproshayka-bot-messages-");

    [TearDown]
    public void TearDown() => _directory.Delete(recursive: true);

    [TestCase("welcome")]
    [TestCase("farewell")]
    [TestCase("connect")]
    [TestCase("disconnect")]
    [TestCase("stream-start")]
    [TestCase("stream-stop")]
    [TestCase("stream-stats")]
    [TestCase("broadcast")]
    [TestCase("punishment")]
    [TestCase("reward")]
    [TestCase("poll-start")]
    [TestCase("poll-progress")]
    [TestCase("poll-end")]
    [TestCase("poll-terminated")]
    [TestCase("poll-archived")]
    public void RuleWritesItsOwnTemplateAndReloadsFromTheSameStore(string key)
    {
        var rules = CreateRules();
        Assert.That(rules, Has.Count.EqualTo(15));
        var rule = rules.Single(item => item.Key == key);
        var previous = rules.ToDictionary(item => item.Key, item => item.Read(), StringComparer.Ordinal);
        var saved = previous[key];
        var changed = saved with { Text = "Новый текст", Interval = saved.Interval + 1 };

        Assert.That(rule.Save(saved, changed), Is.True);

        var reread = CreateRules().ToDictionary(item => item.Key, item => item.Read(), StringComparer.Ordinal);
        Assert.That(reread[key], Is.EqualTo(changed));
        foreach (var other in previous.Where(item => item.Key != key))
        {
            Assert.That(reread[other.Key], Is.EqualTo(other.Value), other.Key);
        }
    }

    [Test]
    public void LegacySettingsKeepStoredTextsAndSharedFlags()
    {
        File.WriteAllText(Path.Combine(_directory.FullName, "settings.json"), """
            {
              "twitch": {
                "messages": { "welcomeEnabled": true, "welcome": "Старое приветствие, {username}", "donateCommandMessage": "Старый донат" },
                "autoBroadcast": { "streamStatusNotificationsEnabled": true, "streamStartMessage": "Старый старт" }
              }
            }
            """);
        var settings = CreateSettings();
        var rules = BotMessageRule.Create(settings, CreatePolls());
        var welcome = rules.Single(rule => rule.Key == "welcome");
        var previous = welcome.Read();
        Assert.That(previous, Is.EqualTo(new BotMessageValues(true, "Старое приветствие, {username}")));
        Assert.That(welcome.Save(previous, previous with { Text = " " }), Is.True);

        var start = rules.Single(rule => rule.Key == "stream-start");
        var before = start.Read();
        Assert.That(start.Save(before, before with { Enabled = false, Text = " " }), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(settings.Current.ChatMessages.DonateCommandMessage, Is.EqualTo("Старый донат"));
            Assert.That(welcome.Read().Text, Is.EqualTo(new MessageSettings().Welcome));
            Assert.That(start.Read().Text, Is.Empty);
            Assert.That(rules.Single(rule => rule.Key == "stream-stop").Read().Enabled, Is.False);
            Assert.That(rules.Single(rule => rule.Key == "stream-stats").Read().Enabled, Is.False);
        });
    }

    [TestCase("settings.json", "reward")]
    [TestCase("polls.json", "poll-start")]
    public async Task RefusedWriteKeepsLastPersistedRuntimeValue(string fileName, string key)
    {
        var gate = new SettingsWriteGate();
        var rule = CreateRules(gate).Single(item => item.Key == key);
        var initial = rule.Read();
        var saved = initial with { Text = "Записанный текст" };
        Assert.That(rule.Save(initial, saved), Is.True);

        await gate.RunExternalWriteAsync(() => Task.FromResult(true), _ => new[] { fileName });
        var written = rule.Save(saved, saved with { Enabled = !saved.Enabled, Text = "Правка с отказом" });

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.False);
            Assert.That(rule.Read(), Is.EqualTo(saved));
            Assert.That(CreateRules().Single(item => item.Key == key).Read(), Is.EqualTo(saved));
        });
    }

    [TestCase("settings.json", "welcome")]
    [TestCase("polls.json", "poll-end")]
    public void FailedDiskWriteLeavesTheStoreAtTheLastSavedValue(string fileName, string key)
    {
        var rule = CreateRules().Single(item => item.Key == key);
        var initial = rule.Read();
        var saved = initial with { Text = "Сохранённый текст" };
        Assert.That(rule.Save(initial, saved), Is.True);

        var path = Path.Combine(_directory.FullName, fileName);
        var oldFile = path + ".original";
        File.Move(path, oldFile);
        Directory.CreateDirectory(path);

        Assert.That(() => rule.Save(saved, saved with { Text = "Утерянная правка" }), Throws.Exception);
        Assert.That(rule.Read(), Is.EqualTo(saved));

        Directory.Delete(path);
        File.Move(oldFile, path);
        Assert.That(rule.Save(saved, saved with { Enabled = !saved.Enabled }), Is.True);
        Assert.That(CreateRules().Single(item => item.Key == key).Read().Text, Is.EqualTo(saved.Text));
    }

    private IReadOnlyList<BotMessageRule> CreateRules(SettingsWriteGate? gate = null)
        => BotMessageRule.Create(CreateSettings(gate), CreatePolls(gate));

    [TestCase("punishment")]
    [TestCase("reward")]
    public void PointExamplesUseTheSameValuesAsTheChatFormatter(string key)
    {
        var settings = CreateSettings();
        var rule = BotMessageRule.Create(settings, CreatePolls()).Single(item => item.Key == key);
        settings.Mutate(value => value.Ranks.PointTerm.Many = "жетонов");

        const string template = "{username}, {count}, {points}";
        var example = MessageTemplate.For(template);
        foreach (var (placeholder, value) in rule.ExampleValues)
        {
            example = example.With(placeholder, value);
        }

        Assert.That(example.Render(), Is.EqualTo(
            UserPointsManagementService.FormatMessage(template, "Алекс", 50, settings.Current.Ranks.PointTerm)));
    }

    [TestCase(null, false)]
    [TestCase(ChatDeliveryStatus.Sent, false)]
    [TestCase(ChatDeliveryStatus.Rejected, true)]
    public void PartialDeliveryRequiresAnUnsuccessfulOutcome(ChatDeliveryStatus? otherStatus, bool partial)
    {
        var completed = new ChatAccountSendResult(ChatEnqueueResult.Queued, ChatDeliveryStatus.Sent, 1);
        ChatAccountSendResult? other = otherStatus is { } status ? new(ChatEnqueueResult.Queued, status) : null;
        Dictionary<StreamingPlatform, BroadcastPlatformDelivery> results = new()
        {
            [StreamingPlatform.Twitch] = new(ChatEnqueueResult.Queued, false, completed),
            [StreamingPlatform.YouTube] = new(ChatEnqueueResult.Queued, other is null, other),
        };

        var label = BroadcastDeliveryText.Format(results, new ChatPlatformRegistry([]));

        Assert.That(label, Does.Contain("доставка подтверждена"));
        Assert.That(label!.StartsWith("Частичный результат", StringComparison.Ordinal), Is.EqualTo(partial));
    }

    private SettingsManager CreateSettings(SettingsWriteGate? gate = null)
        => new(NullLogger<SettingsManager>.Instance, Path.Combine(_directory.FullName, "settings.json"), gate);

    private PollsStore CreatePolls(SettingsWriteGate? gate = null)
        => new(filePath: Path.Combine(_directory.FullName, "polls.json"), gate: gate);
}
