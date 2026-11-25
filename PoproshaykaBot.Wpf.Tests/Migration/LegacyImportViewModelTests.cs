using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Wpf.ViewModels.Migration;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Migration;

[TestFixture]
public class LegacyImportViewModelTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "poprosh-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    [TestCase(true, 1, false)]
    [TestCase(false, 0, true)]
    public async Task A_chosen_folder_becomes_a_card_only_when_it_holds_data(bool seedData, int expectedSources, bool expectsNotice)
    {
        if (seedData)
        {
            await File.WriteAllTextAsync(Path.Combine(_folder, "settings.json"), """{"twitch":{"channel":"bobito"}}""");
        }

        var viewModel = new LegacyImportViewModel([], null, false, new FakeFilePicker(_folder), NullLogger.Instance);

        await viewModel.PickFolderCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Sources, Has.Count.EqualTo(expectedSources),
                "Папка без данных бота не должна превращаться в карточку источника.");
            Assert.That(viewModel.PickerNotice is not null, Is.EqualTo(expectsNotice),
                "О непригодной папке сообщает строка под списком, а не отдельное окно.");
            Assert.That(viewModel.SelectedSource is not null, Is.EqualTo(seedData),
                "Пригодная папка выбирается сразу, иначе кнопка переноса осталась бы недоступной.");
        });
    }

    [TestCase(LegacyDataSourceKind.CurrentBaseDirectory, true, false)]
    [TestCase(LegacyDataSourceKind.SelectedFolder, false, false)]
    [TestCase(LegacyDataSourceKind.SelectedFolder, true, true)]
    public void Replacing_files_is_offered_only_for_the_selected_source_with_replaceable_files(
        LegacyDataSourceKind kind, bool conflict, bool expected)
    {
        var relative = Path.Combine("settings", "settings.json");
        var source = Summary(_folder, kind, relative);
        var target = conflict ? Summary(AppPaths.BaseDirectory, LegacyDataSourceKind.CurrentBaseDirectory, relative) : null;
        var viewModel = new LegacyImportViewModel([source], target, false, new FakeFilePicker(_folder), NullLogger.Instance);
        viewModel.SelectedSource = viewModel.Sources[0];

        Assert.That(viewModel.CanOverwriteExisting, Is.EqualTo(expected));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Dismissing_the_offer_belongs_to_the_startup_window_only(bool isSettingsEntry, bool expectsDismissCheckbox)
    {
        var viewModel = new LegacyImportViewModel([], null, isSettingsEntry, new FakeFilePicker(_folder), NullLogger.Instance);

        Assert.That(viewModel.CanDismiss, Is.EqualTo(expectsDismissCheckbox),
            "Из настроек окно открывают сами, и «Больше не предлагать» там нечего отключать.");
    }

    [TestCase(false, true, true)]
    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    public async Task Setup_can_be_requested_only_after_a_startup_import_changes_files(
        bool isSettingsEntry, bool copiedFile, bool expected)
    {
        const string FileName = "settings/settings.json";
        var source = Summary(_folder, LegacyDataSourceKind.SelectedFolder, FileName);
        var viewModel = new LegacyImportViewModel([source], null, isSettingsEntry, new FakeFilePicker(_folder), NullLogger.Instance,
            import: (path, _, _) => new()
            {
                SourcePath = path,
                TargetPath = Path.Combine(_folder, "target"),
                CopiedFiles = copiedFile ? [FileName] : [],
                SkippedFiles = [],
                Failures = [],
            });
        viewModel.SelectedSource = viewModel.Sources[0];

        Assert.That(viewModel.ConfigureCommand.CanExecute(null), Is.False);
        await viewModel.ImportCommand.ExecuteAsync(null);

        var closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.CanConfigure, Is.EqualTo(expected));
            Assert.That(viewModel.ConfigureCommand.CanExecute(null), Is.EqualTo(expected));
            Assert.That(viewModel.ConfigureRequested, Is.False);
        }

        viewModel.ConfigureCommand.Execute(null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.ConfigureRequested, Is.EqualTo(expected));
            Assert.That(closeRequested, Is.EqualTo(expected));
        }
    }

    [Test]
    public void Replacing_files_is_offered_for_store_generated_by_monolith_split()
    {
        File.WriteAllText(Path.Combine(_folder, "settings.json"), """{"twitch":{"botUsername":"bot"}}""");
        var source = LegacyDataScanner.Inspect(_folder, NullLogger.Instance)!;
        var target = Summary(AppPaths.BaseDirectory, LegacyDataSourceKind.CurrentBaseDirectory, Path.Combine("settings", "accounts.json"));
        var viewModel = new LegacyImportViewModel([source], target, false, new FakeFilePicker(_folder), NullLogger.Instance);
        viewModel.SelectedSource = viewModel.Sources[0];

        Assert.That(viewModel.CanOverwriteExisting, Is.True);
    }

    [Test]
    public async Task ImportAsync_RevokesChatHistoryCacheWrites_WhenChatHistoryCopied()
    {
        var sourceDir = Path.Combine(_folder, "source");
        var targetDir = Path.Combine(_folder, "target");
        Directory.CreateDirectory(Path.Combine(sourceDir, "chat-history"));
        Directory.CreateDirectory(targetDir);

        var cacheFile = new string('b', 64) + ".json";
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "chat-history", cacheFile), """{"schemaVersion":1,"platform":1,"channelId":"1","messages":[]}""");

        using var cacheStore = new ChatHistoryCacheStore(cacheDirectory: Path.Combine(targetDir, "chat-history"));
        var summary = LegacyDataScanner.Inspect(sourceDir, NullLogger.Instance)!;

        var viewModel = new LegacyImportViewModel(
            [summary],
            target: null,
            isSettingsEntry: true,
            new FakeFilePicker(_folder),
            NullLogger.Instance,
            chatHistoryCache: cacheStore,
            import: (src, overwrite, log) => LegacyDataImporter.Import(src, targetDir, overwrite, log));

        viewModel.SelectedSource = viewModel.Sources[0];
        await viewModel.ImportCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.IsCompleted, Is.True);
            Assert.That(viewModel.Result?.CopiedChatHistory, Is.True);
            Assert.That(cacheStore.IsWriteRevoked, Is.True);
            Assert.That(viewModel.ResultRestartNotice, Does.Contain("кэш истории чата"));
        }
    }

    [Test]
    public async Task Picking_new_and_existing_folders_changes_the_actual_import_source()
    {
        var paths = new[] { Path.Combine(_folder, "first"), Path.Combine(_folder, "second"), Path.Combine(_folder, "empty") };
        foreach (var folder in paths)
        {
            Directory.CreateDirectory(folder);
        }

        foreach (var folder in paths.Take(2))
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "settings.json"), """{"twitch":{"channel":"bobito"}}""");
        }

        var picker = new FakeFilePicker(paths[1]);
        var first = LegacyDataScanner.Inspect(paths[0])!;
        var target = Path.Combine(_folder, "target");
        var viewModel = new LegacyImportViewModel([first], null, false, picker, NullLogger.Instance,
            import: (path, overwrite, log) => LegacyDataImporter.Import(path, target, overwrite, log));
        viewModel.SelectedSource = viewModel.Sources[0];
        await viewModel.PickFolderCommand.ExecuteAsync(null);
        Assert.That(viewModel.SelectedSource?.Path, Is.EqualTo(paths[1]));

        picker.Folder = paths[2];
        await viewModel.PickFolderCommand.ExecuteAsync(null);
        Assert.That(viewModel.SelectedSource?.Path, Is.EqualTo(paths[1]));
        Assert.That(viewModel.PickerNotice, Is.Not.Null);

        picker.Folder = paths[0];
        await viewModel.PickFolderCommand.ExecuteAsync(null);
        await viewModel.ImportCommand.ExecuteAsync(null);
        Assert.That(viewModel.Sources, Has.Count.EqualTo(2));
        Assert.That(viewModel.Result?.SourcePath, Is.EqualTo(paths[0]));
    }

    private static LegacyDataSummary Summary(string path, LegacyDataSourceKind kind, string relative) => new()
    {
        SourcePath = path,
        Kind = kind,
        HasMonolithicSettings = false,
        HasSettingsDirectory = true,
        HasOAuthTokens = false,
        Files = [new(Path.GetFileName(relative), Path.Combine(path, relative), relative, 100, DateTime.UtcNow)],
    };

    private sealed class FakeFilePicker(string folder) : IFilePicker
    {
        public string Folder { get; set; } = folder;

        public string? PickFolder(string title, string? initialDirectory = null)
        {
            return Folder;
        }

        public string? PickFile(FileOpenRequest request)
        {
            return null;
        }

        public string? SaveFile(FileSaveRequest request)
        {
            return null;
        }
    }
}
