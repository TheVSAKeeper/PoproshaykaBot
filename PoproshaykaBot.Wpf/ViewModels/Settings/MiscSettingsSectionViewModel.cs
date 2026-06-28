using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class MiscSettingsSectionViewModel : ObservableObject, IDisposable
{
    private static readonly JsonSerializerOptions ImportJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly SettingsManager _settingsManager;
    private readonly BroadcastProfilesManager _broadcastProfiles;
    private readonly IDialogService _dialogService;

    public MiscSettingsSectionViewModel(SettingsManager settingsManager, BroadcastProfilesManager broadcastProfiles, IDialogService dialogService)
    {
        _settingsManager = settingsManager;
        _broadcastProfiles = broadcastProfiles;
        _dialogService = dialogService;
    }

    public void Dispose()
    {
    }

    [RelayCommand]
    private void ShowAbout()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Неизвестно";

        _dialogService.Info("О программе", $"PoproshaykaBot\n\nВерсия: {version}\n\nTwitch бот для стримеров");
    }

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        try
        {
            var dir = AppPaths.BaseDirectory;

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _dialogService.Error("Ошибка", $"Ошибка открытия папки с настройками: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ResetAllSettings()
    {
        if (!_dialogService.ConfirmWarning("Сброс настроек", "Вы уверены, что хотите сбросить все настройки к значениям по умолчанию?\n\nЭто действие нельзя отменить."))
        {
            return;
        }

        try
        {
            _settingsManager.SaveSettings(new AppSettings());

            _dialogService.Info("Сброс настроек", "Настройки успешно сброшены к значениям по умолчанию.\n\nПерезапустите приложение для применения изменений.");
        }
        catch (Exception ex)
        {
            _dialogService.Error("Ошибка", $"Ошибка сброса настроек: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ImportBroadcastProfiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Импорт профилей трансляций из JSON",
            Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        List<ExternalTwitchProfile>? incoming;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            incoming = JsonSerializer.Deserialize<List<ExternalTwitchProfile>>(json, ImportJsonOptions);
        }
        catch (Exception ex)
        {
            _dialogService.Error("Ошибка импорта", $"Не удалось прочитать файл: {ex.Message}");
            return;
        }

        if (incoming is null || incoming.Count == 0)
        {
            _dialogService.Info("Импорт", "В файле нет профилей.");
            return;
        }

        var imported = 0;
        var skipped = 0;
        var existingNames = new HashSet<string>(
            _broadcastProfiles.GetAll().Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var external in incoming)
        {
            if (string.IsNullOrWhiteSpace(external.Name))
            {
                skipped++;
                continue;
            }

            var name = external.Name;
            var suffix = 2;

            while (existingNames.Contains(name))
            {
                name = $"{external.Name} ({suffix++})";
            }

            var profile = new BroadcastProfile
            {
                Name = name,
                Title = external.Title ?? string.Empty,
                GameId = external.CategoryId ?? string.Empty,
            };

            try
            {
                _broadcastProfiles.Upsert(profile);
                existingNames.Add(name);
                imported++;
            }
            catch (InvalidOperationException)
            {
                skipped++;
            }
        }

        _dialogService.Info("Импорт профилей", $"Импортировано: {imported}. Пропущено: {skipped}.");
    }

    private sealed record ExternalTwitchProfile(string? Name, string? Title, string? CategoryId);
}
