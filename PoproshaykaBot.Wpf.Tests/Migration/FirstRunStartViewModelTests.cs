using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Wpf.ViewModels.Migration;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Migration;

[TestFixture]
public sealed class FirstRunStartViewModelTests
{
    private string _root = string.Empty;
    private string _sourceDir = string.Empty;
    private string _targetDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "pb-first-run-" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_root, "old-bot");
        _targetDir = Path.Combine(_root, "new-bot");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_targetDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void StartFreshCommand_CallsDismissCallbackWithSelectedLocationAndClosesWithStartFresh()
    {
        var dismissed = false;
        FirstRunStorageLocation? receivedLocation = null;
        var closeResults = new List<bool>();
        var viewModel = new FirstRunStartViewModel(
            [],
            new StubFilePicker(null),
            NullLogger.Instance,
            import: (src, _, logger) => LegacyDataImporter.Import(src, _targetDir, overwrite: false, logger),
            onStartFresh: location =>
            {
                dismissed = true;
                receivedLocation = location;
                return true;
            });
        viewModel.RequestClose += (_, result) => closeResults.Add(result);

        Assert.That(viewModel.SelectedStorageLocation, Is.EqualTo(FirstRunStorageLocation.Portable));
        Assert.That(viewModel.IsPortableStorageSelected, Is.True);

        viewModel.IsAppDataStorageSelected = true;
        viewModel.StartFreshCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissed, Is.True);
            Assert.That(receivedLocation, Is.EqualTo(FirstRunStorageLocation.AppData));
            Assert.That(viewModel.Outcome, Is.EqualTo(FirstRunStartOutcome.StartFresh));
            Assert.That(closeResults, Is.EqualTo(new[] { false }));
        }
    }

    [Test]
    public void StartFreshCommand_WhenAppDataSwitchBlocked_StaysOpenWithNotice()
    {
        var closeResults = new List<bool>();
        var viewModel = new FirstRunStartViewModel(
            [],
            new StubFilePicker(null),
            NullLogger.Instance,
            import: (src, _, logger) => LegacyDataImporter.Import(src, _targetDir, overwrite: false, logger),
            onStartFresh: _ => throw new AppDataAlreadyRunningException("Другой экземпляр уже запущен."));
        viewModel.RequestClose += (_, result) => closeResults.Add(result);

        viewModel.IsAppDataStorageSelected = true;
        viewModel.StartFreshCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(closeResults, Is.Empty);
            Assert.That(viewModel.StorageErrorNotice, Does.Contain("Другой экземпляр"));
        }
    }

    [Test]
    public async Task PickFolderCommand_ShowsHumanReadableInvalidFolderState_AndAllowsPickingValidFolder()
    {
        var emptyDir = Path.Combine(_root, "empty");
        Directory.CreateDirectory(emptyDir);
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "settings.json"), """{"twitch":{"channel":"bobito"}}""");

        var picker = new StubFilePicker(emptyDir);
        var viewModel = new FirstRunStartViewModel(
            [],
            picker,
            NullLogger.Instance,
            import: (src, _, logger) => LegacyDataImporter.Import(src, _targetDir, overwrite: false, logger),
            onStartFresh: _ => true);

        await viewModel.PickFolderCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.CurrentStep, Is.EqualTo(FirstRunStartStep.InvalidFolder));
            Assert.That(viewModel.IsInvalidFolderStep, Is.True);
            Assert.That(viewModel.InvalidFolderMessage, Does.Contain("пуста").And.Contain("settings.json"));
            Assert.That(viewModel.InvalidFolderMessage, Does.Not.Contain("Exception").And.Not.Contain("StackTrace"));
        }

        picker.Folder = _sourceDir;
        await viewModel.PickFolderCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.CurrentStep, Is.EqualTo(FirstRunStartStep.Choice));
            Assert.That(viewModel.SelectedSource?.Path, Is.EqualTo(_sourceDir));
            Assert.That(viewModel.InvalidFolderMessage, Is.Null);
        }
    }

    [Test]
    public async Task ImportCommand_FullProfile_KeepsOldFolderReadOnly_ReloadsContainer_AndSupportsBothExits()
    {
        await SeedFullOldFolderAsync(_sourceDir);
        var beforeTimestamps = SnapshotTimestamps(_sourceDir);

        var candidate = LegacyDataScanner.Inspect(_sourceDir, NullLogger.Instance)!;
        var reloadCalled = false;
        FirstRunStorageLocation? importedLocation = null;

        var viewModel = new FirstRunStartViewModel(
            [candidate],
            new StubFilePicker(null),
            NullLogger.Instance,
            import: (src, location, logger) =>
            {
                importedLocation = location;
                return LegacyDataImporter.Import(src, _targetDir, overwrite: false, logger);
            },
            onStartFresh: _ => true,
            afterImportAsync: (_, _) =>
            {
                reloadCalled = true;
                return Task.CompletedTask;
            });

        await viewModel.ImportCommand.ExecuteAsync(null);

        var afterTimestamps = SnapshotTimestamps(_sourceDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(importedLocation, Is.EqualTo(FirstRunStorageLocation.Portable));
            Assert.That(reloadCalled, Is.True);
            Assert.That(viewModel.CurrentStep, Is.EqualTo(FirstRunStartStep.Summary));
            Assert.That(viewModel.IsPartialSummary, Is.False);
            Assert.That(viewModel.TransferredItems.Select(static i => i.Title), Is.EqualTo(new[]
            {
                "Профили эфира",
                "Оверлей и локальный порт",
                "OBS и аудиоисточники",
                "Авто-рассылка",
                "Фразы и ранги",
                "Статистика зрителей и сессий",
            }));
            Assert.That(afterTimestamps, Is.EqualTo(beforeTimestamps), "Исходная папка должна остаться только для чтения");
        }

        var closed = new List<bool>();
        viewModel.RequestClose += (_, result) => closed.Add(result);

        viewModel.ConnectYouTubeCommand.Execute(null);
        Assert.That(viewModel.Outcome, Is.EqualTo(FirstRunStartOutcome.ConnectYouTube));

        viewModel.OpenDashboardCommand.Execute(null);
        Assert.That(viewModel.Outcome, Is.EqualTo(FirstRunStartOutcome.OpenDashboard));
        Assert.That(closed, Is.EqualTo(new[] { true, true }));
    }

    [Test]
    public async Task ImportCommand_PartialImport_ListsTransferredBlocksAndMissingItems()
    {
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "settings.json"), """
        {
          "twitch": {
            "channel": "bobito",
            "httpServerPort": 8081,
            "autoBroadcast": { "autoBroadcastEnabled": false, "broadcastIntervalMinutes": 15 }
          }
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "users_statistics.json"), """{"1":{"name":"viewer1"}}""");

        var candidate = LegacyDataScanner.Inspect(_sourceDir, NullLogger.Instance)!;
        var viewModel = new FirstRunStartViewModel(
            [candidate],
            new StubFilePicker(null),
            NullLogger.Instance,
            import: (src, _, logger) => LegacyDataImporter.Import(src, _targetDir, overwrite: false, logger),
            onStartFresh: _ => true);

        await viewModel.ImportCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.CurrentStep, Is.EqualTo(FirstRunStartStep.Summary));
            Assert.That(viewModel.IsPartialSummary, Is.True);
            Assert.That(viewModel.TransferredItems.Any(static i => i.Title == "Оверлей и локальный порт"), Is.True);
            Assert.That(viewModel.TransferredItems.Any(static i => i.Title == "Статистика зрителей и сессий"), Is.True);
            Assert.That(viewModel.HasAttentionItems, Is.True);
            Assert.That(viewModel.AttentionItems.Any(static i => i.Title == "Профили эфира"), Is.True);
            Assert.That(viewModel.AttentionItems.Any(static i => i.Title == "OBS и аудиоисточники"), Is.True);
            Assert.That(viewModel.AttentionItems.Any(static i => i.Title == "Вход в Twitch"), Is.True);
        }
    }

    [Test]
    public async Task ImportCommand_WhenReloadThrows_StillShowsSummaryWithRestartNotice()
    {
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "settings.json"), """{"twitch":{"channel":"bobito"}}""");
        var candidate = LegacyDataScanner.Inspect(_sourceDir, NullLogger.Instance)!;

        var viewModel = new FirstRunStartViewModel(
            [candidate],
            new StubFilePicker(null),
            NullLogger.Instance,
            import: (src, _, logger) => LegacyDataImporter.Import(src, _targetDir, overwrite: false, logger),
            onStartFresh: _ => true,
            afterImportAsync: (_, _) => throw new InvalidOperationException("reload failed"));

        await viewModel.ImportCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.CurrentStep, Is.EqualTo(FirstRunStartStep.Summary));
            Assert.That(viewModel.ReloadNotice, Does.Contain("перезапустите"));
        }
    }

    [Test]
    public async Task ImportCommand_WhenAppDataSelected_PassesAppDataLocationToImportAndAfterImport()
    {
        await SeedFullOldFolderAsync(_sourceDir);
        var candidate = LegacyDataScanner.Inspect(_sourceDir, NullLogger.Instance)!;
        FirstRunStorageLocation? importedLocation = null;
        FirstRunStorageLocation? reloadedLocation = null;

        var viewModel = new FirstRunStartViewModel(
            [candidate],
            new StubFilePicker(null),
            NullLogger.Instance,
            import: (src, location, logger) =>
            {
                importedLocation = location;
                return LegacyDataImporter.Import(src, _targetDir, overwrite: false, logger);
            },
            onStartFresh: _ => true,
            afterImportAsync: (_, location) =>
            {
                reloadedLocation = location;
                return Task.CompletedTask;
            });

        viewModel.IsAppDataStorageSelected = true;
        await viewModel.ImportCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(importedLocation, Is.EqualTo(FirstRunStorageLocation.AppData));
            Assert.That(reloadedLocation, Is.EqualTo(FirstRunStorageLocation.AppData));
            Assert.That(viewModel.CurrentStep, Is.EqualTo(FirstRunStartStep.Summary));
        }
    }

    [Test]
    public async Task ImportCommand_WhenBackupRestoreFails_ShowsBackupPathInInvalidFolderStep()
    {
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "settings.json"), """{"twitch":{"channel":"bobito"}}""");
        var candidate = LegacyDataScanner.Inspect(_sourceDir, NullLogger.Instance)!;

        var viewModel = new FirstRunStartViewModel(
            [candidate],
            new StubFilePicker(null),
            NullLogger.Instance,
            import: (_, _, _) => throw new AppDataBackupRestoreFailedException(@"Резервная копия осталась в C:\AppData\PoproshaykaBot.backup-20261127-120000"),
            onStartFresh: _ => true);

        await viewModel.ImportCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.CurrentStep, Is.EqualTo(FirstRunStartStep.InvalidFolder));
            Assert.That(viewModel.InvalidFolderMessage, Does.Contain("PoproshaykaBot.backup-20261127-120000"));
        }
    }

    [Test]
    public void RemapSourceAfterAppDataBackup_RemapsRootAndSubfolder_AndLeavesSiblingAndUnrelatedUntouched()
    {
        var appDataDir = Path.Combine(_root, "AppData", "PoproshaykaBot");
        var backupDir = Path.Combine(_root, "AppData", "PoproshaykaBot.backup-20261127-120000");
        var subfolder = Path.Combine(appDataDir, "profiles", "old");
        var sibling = Path.Combine(_root, "AppData", "PoproshaykaBot2");
        var unrelated = Path.Combine(_root, "OtherBot");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(App.RemapSourceAfterAppDataBackup(appDataDir, appDataDir, backupDir), Is.EqualTo(backupDir));
            Assert.That(App.RemapSourceAfterAppDataBackup(subfolder, appDataDir, backupDir), Is.EqualTo(Path.Combine(backupDir, "profiles", "old")));
            Assert.That(App.RemapSourceAfterAppDataBackup(sibling, appDataDir, backupDir), Is.EqualTo(sibling));
            Assert.That(App.RemapSourceAfterAppDataBackup(unrelated, appDataDir, backupDir), Is.EqualTo(unrelated));
            Assert.That(App.RemapSourceAfterAppDataBackup(appDataDir, appDataDir, null), Is.EqualTo(appDataDir));
        }
    }

    [Test]
    public async Task TryRestoreAppDataFromBackup_RestoresOriginalFilesWhenAppDataIsCleared()
    {
        var appDataDir = Path.Combine(_root, "AppData", "PoproshaykaBot");
        var backupDir = Path.Combine(_root, "AppData", "PoproshaykaBot.backup-20261127-120000");
        Directory.CreateDirectory(appDataDir);
        Directory.CreateDirectory(backupDir);
        await File.WriteAllTextAsync(Path.Combine(backupDir, "settings.json"), """{"twitch":{"channel":"restored"}}""");

        var restored = App.TryRestoreAppDataFromBackup(appDataDir, backupDir, NullLogger.Instance);

        var content = await File.ReadAllTextAsync(Path.Combine(appDataDir, "settings.json"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored, Is.True);
            Assert.That(Directory.Exists(backupDir), Is.False);
            Assert.That(File.Exists(Path.Combine(appDataDir, "settings.json")), Is.True);
            Assert.That(content, Does.Contain("restored"));
        }
    }

    private static async Task SeedFullOldFolderAsync(string directory)
    {
        var settingsDir = Path.Combine(directory, "settings");
        Directory.CreateDirectory(settingsDir);

        await File.WriteAllTextAsync(Path.Combine(settingsDir, "settings.json"), """
        {
          "twitch": {
            "channel": "bobito",
            "clientId": "cid",
            "clientSecret": "sec",
            "httpServerPort": 8080,
            "autoBroadcast": { "autoBroadcastEnabled": true, "broadcastIntervalMinutes": 15 }
          },
          "ranks": {
            "ranks": [
              { "symbol": "♛", "name": "ФЕРЗЬ", "minPoints": 100 }
            ]
          }
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(settingsDir, "accounts.json"), """
        {
          "botAccount": {
            "login": "bot",
            "accessToken": "tok",
            "refreshToken": "ref",
            "scopes": ["chat:read","chat:edit","user:read:chat","user:write:chat","user:bot"],
            "storedScopes": ["chat:read","chat:edit","user:read:chat","user:write:chat","user:bot"]
          }
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(settingsDir, "broadcast-profiles.json"), """{"profiles":[{"name":"Основной"}]}""");
        await File.WriteAllTextAsync(Path.Combine(settingsDir, "obs-chat.json"), """{"fontSize":15}""");
        await File.WriteAllTextAsync(Path.Combine(settingsDir, "obs-integration.json"), """{"enabled":true,"host":"127.0.0.1","port":4455,"dashboardMicrophoneName":"Микрофон","dashboardSourceNames":["Микрофон"]}""");
        await File.WriteAllTextAsync(Path.Combine(settingsDir, "commands.json"), """{"commands":{"!hi":{"enabled":true}}}""");
        await File.WriteAllTextAsync(Path.Combine(directory, "users_statistics.json"), """{"1":{"name":"viewer1"}}""");
        await File.WriteAllTextAsync(Path.Combine(directory, "stream_sessions.json"), """[{"channel":"bobito"}]""");
    }

    private static Dictionary<string, DateTime> SnapshotTimestamps(string directory)
    {
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, File.GetLastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class StubFilePicker(string? folder) : IFilePicker
    {
        public string? Folder { get; set; } = folder;

        public string? PickFolder(string title, string? initialDirectory = null) => Folder;

        public string? PickFile(FileOpenRequest request) => null;

        public string? SaveFile(FileSaveRequest request) => null;
    }
}
