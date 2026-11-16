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
    public void A_chosen_folder_becomes_a_card_only_when_it_holds_data(bool seedData, int expectedSources, bool expectsNotice)
    {
        if (seedData)
        {
            File.WriteAllText(Path.Combine(_folder, "settings.json"), """{"twitch":{"channel":"bobito"}}""");
        }

        var viewModel = new LegacyImportViewModel([], false, false, new FakeFilePicker(_folder), NullLogger.Instance);

        viewModel.PickFolderCommand.Execute(null);

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

    [TestCase(true)]
    [TestCase(false)]
    public void Replacing_files_is_offered_only_where_there_is_something_to_replace(bool hasOwnData)
    {
        var viewModel = new LegacyImportViewModel([], hasOwnData, false, new FakeFilePicker(_folder), NullLogger.Instance);

        Assert.That(viewModel.CanOverwriteExisting, Is.EqualTo(hasOwnData),
            "На чистой установке заменять нечего, и флажок замены там только сбивает с толку.");
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Dismissing_the_offer_belongs_to_the_startup_window_only(bool isSettingsEntry, bool expectsDismissCheckbox)
    {
        var viewModel = new LegacyImportViewModel([], false, isSettingsEntry, new FakeFilePicker(_folder), NullLogger.Instance);

        Assert.That(viewModel.CanDismiss, Is.EqualTo(expectsDismissCheckbox),
            "Из настроек окно открывают сами, и «Больше не предлагать» там нечего отключать.");
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
            hasOwnData: true,
            isSettingsEntry: true,
            new FakeFilePicker(_folder),
            NullLogger.Instance,
            chatHistoryCache: cacheStore,
            import: (src, overwrite, log) => LegacyDataImporter.Import(src, targetDir, overwrite, log));

        viewModel.Sources[0].IsSelected = true;
        await viewModel.ImportCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.IsCompleted, Is.True);
            Assert.That(viewModel.Result?.CopiedChatHistory, Is.True);
            Assert.That(cacheStore.IsWriteRevoked, Is.True);
            Assert.That(viewModel.ResultRestartNotice, Does.Contain("кэш истории чата"));
        }
    }

    private sealed class FakeFilePicker(string folder) : IFilePicker
    {
        public string? PickFolder(string title, string? initialDirectory = null)
        {
            return folder;
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
