using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.ViewModels;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class CommandRowTests
{
    private sealed class FakeCommand : IChatCommand
    {
        public string Canonical => "помощь";

        public IReadOnlyCollection<string> Aliases => ["help", "h"];

        public string Description => "список команд бота";

        public bool CanExecute(CommandContext context) => true;

        public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult<OutgoingMessage?>(null);
        }
    }

    private sealed class RestrictedCommand(string canonical = "x2illson", bool canExecute = true) : IChatCommand
    {
        public string Canonical => canonical;

        public IReadOnlyCollection<string> Aliases => [];

        public string Description => "курс монеты для своих";

        public bool IsRestrictedToAllowedUsers => true;

        public bool CanExecute(CommandContext context) => canExecute;

        public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult<OutgoingMessage?>(null);
        }
    }

    private sealed class ModeratorsCommand : IChatCommand
    {
        public string Canonical => "игра";

        public IReadOnlyCollection<string> Aliases => [];

        public string Description => "сменить категорию";

        public bool CanExecute(CommandContext context) => context.IsModerator || context.IsBroadcaster;

        public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult<OutgoingMessage?>(null);
        }
    }

    private static CommandRowViewModel Row()
    {
        return new(new FakeCommand(), "!", CommandAccess.Everyone);
    }

    [Test]
    public void Reads_the_inherited_target_from_the_common_default()
    {
        var settings = new CommandSettings
        {
            DefaultResponseTarget = CommandResponseTarget.Overlay,
        };

        var row = Row();
        row.ApplySettings(settings);

        Assert.Multiple(() =>
        {
            Assert.That(row.TargetOption, Is.SameAs(CommandResponseTargetOption.Inherit));
            Assert.That(row.IsTargetInherited, Is.True);
            Assert.That(row.EffectiveTargetText, Is.EqualTo(CommandResponseTargetOption.Overlay.Title));
        });
    }

    [TestCase(CommandResponseTarget.Chat)]
    [TestCase(CommandResponseTarget.Overlay)]
    [TestCase(CommandResponseTarget.None)]
    [TestCase(CommandSettings.KnownTargets)]
    public void Reads_the_own_target_of_a_command(CommandResponseTarget target)
    {
        var settings = new CommandSettings();
        settings.Commands["помощь"] = new()
        {
            ResponseTarget = target,
        };

        var row = Row();
        row.ApplySettings(settings);

        Assert.Multiple(() =>
        {
            Assert.That(row.TargetOption.Target, Is.EqualTo(target));
            Assert.That(row.IsTargetInherited, Is.False);
        });
    }

    [Test]
    public void Reading_the_file_does_not_look_like_a_user_edit()
    {
        var settings = new CommandSettings();
        settings.Commands["помощь"] = new()
        {
            Enabled = false,
            ResponseTarget = CommandResponseTarget.Chat,
        };

        var row = Row();
        var edits = 0;

        row.EnabledChanged += (_, _) => edits++;
        row.TargetChanged += (_, _) => edits++;

        row.ApplySettings(settings);

        Assert.That(edits, Is.Zero, "Загрузка настроек не должна писать их обратно в файл");
    }

    [Test]
    public void A_user_edit_asks_the_page_to_save_it()
    {
        var row = Row();
        var enabledEdits = 0;
        var targetEdits = 0;

        row.EnabledChanged += (_, _) => enabledEdits++;
        row.TargetChanged += (_, _) => targetEdits++;

        row.IsEnabled = false;
        row.TargetOption = CommandResponseTargetOption.Silent;

        Assert.Multiple(() =>
        {
            Assert.That(enabledEdits, Is.EqualTo(1));
            Assert.That(targetEdits, Is.EqualTo(1));
        });
    }

    [TestCase("помощь", true)]
    [TestCase("ПОМОЩЬ", true)]
    [TestCase("!h", true)]
    [TestCase("список", true)]
    [TestCase("донат", false)]
    public void Finds_a_command_by_name_alias_or_description(string query, bool expected)
    {
        Assert.That(Row().Matches(query), Is.EqualTo(expected));
    }

    [Test]
    public void Shows_a_dash_until_the_command_is_called()
    {
        var row = Row();
        row.ApplyUsage(null);

        Assert.Multiple(() =>
        {
            Assert.That(row.HasLastUse, Is.False);
            Assert.That(row.LastUsedAtText, Is.EqualTo("–"));
            Assert.That(row.TotalCountText, Is.EqualTo("0"));
        });
    }

    [Test]
    public void Names_the_caller_of_the_last_use()
    {
        var row = Row();

        row.ApplyUsage(new CommandUsageRecord
        {
            Canonical = "помощь",
            TotalCount = 1284,
            StreamCount = 37,
            LastUsedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastUsedBy = "qp_illson",
        });

        Assert.Multiple(() =>
        {
            Assert.That(row.HasLastUse, Is.True);
            Assert.That(row.LastUsedAtText, Is.EqualTo("5 мин. назад"));
            Assert.That(row.LastUsedByText, Is.EqualTo("qp_illson"));
            Assert.That(row.LastUsedSummary, Does.Contain("qp_illson"));
        });
    }

    [Test]
    public void Права_различают_особый_список_и_берут_префикс_у_процессора()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"poproshayka-commands-{Guid.NewGuid():N}.json");

        try
        {
            var usage = new CommandUsageRepository(TimeProvider.System);
            var settingsStore = new CommandSettingsStore(null, settingsPath);

            var processor = new ChatCommandProcessor(
                [new FakeCommand(), new RestrictedCommand(), new RestrictedCommand("тайное", false), new ModeratorsCommand()],
                settingsStore,
                usage,
                NullLogger<ChatCommandProcessor>.Instance,
                "?");

            var page = new CommandsPageViewModel(
                processor,
                settingsStore,
                usage,
                NullLogger<CommandsPageViewModel>.Instance);

            var everyone = page.Rows.Single(row => row.Canonical == "помощь");
            var restricted = page.Rows.Single(row => row.Canonical == "x2illson");
            var restrictedClosed = page.Rows.Single(row => row.Canonical == "тайное");
            var moderators = page.Rows.Single(row => row.Canonical == "игра");

            Assert.Multiple(() =>
            {
                Assert.That(restricted.Access, Is.EqualTo(CommandAccess.AllowedUsers));
                Assert.That(restricted.AccessText, Is.EqualTo("Особый список"));
                Assert.That(
                    restrictedClosed.Access,
                    Is.EqualTo(CommandAccess.AllowedUsers),
                    "Ни один проб не представляет пользователя из особого списка");
                Assert.That(everyone.Access, Is.EqualTo(CommandAccess.Everyone));
                Assert.That(moderators.Access, Is.EqualTo(CommandAccess.Moderators));
                Assert.That(everyone.Invocation, Is.EqualTo("?помощь"), "Префикс страницы приходит из Core");
                Assert.That(everyone.Aliases, Is.EqualTo(new[] { "?help", "?h" }));
            });
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Test]
    public void A_call_on_the_open_page_grows_the_counters_of_the_same_row()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"poproshayka-commands-{Guid.NewGuid():N}.json");

        try
        {
            var usage = new CommandUsageRepository(TimeProvider.System);
            var settingsStore = new CommandSettingsStore(null, settingsPath);

            var processor = new ChatCommandProcessor(
                [new FakeCommand()],
                settingsStore,
                usage,
                NullLogger<ChatCommandProcessor>.Instance);

            var page = new CommandsPageViewModel(
                processor,
                settingsStore,
                usage,
                NullLogger<CommandsPageViewModel>.Instance);

            var row = page.Rows.Single();
            page.SelectedRow = row;
            row.TargetOption = CommandResponseTargetOption.Silent;

            usage.Track("помощь", "qp_illson");
            usage.Track("помощь", "qp_illson");
            usage.Track("донат", "qp_illson");

            page.RefreshUsage();

            Assert.Multiple(() =>
            {
                Assert.That(page.Rows, Has.Count.EqualTo(1), "Вызов неизвестной команды не добавляет строку");
                Assert.That(page.Rows.Single(), Is.SameAs(row), "Строки не пересобираются");
                Assert.That(page.SelectedRow, Is.SameAs(row), "Выделение сохраняется");
                Assert.That(row.TargetOption, Is.SameAs(CommandResponseTargetOption.Silent), "Правка строки сохраняется");
                Assert.That(row.TotalCount, Is.EqualTo(2));
                Assert.That(row.StreamCount, Is.EqualTo(2));
                Assert.That(row.LastUsedByText, Is.EqualTo("qp_illson"));
                Assert.That(page.SummaryLine, Does.Contain("вызовов 2"));
            });
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }
}
