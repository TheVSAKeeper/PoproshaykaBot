using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class MiscSettingsSectionViewModel : ObservableObject, IDisposable
{
    private static readonly JsonSerializerOptions ImportJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly SettingsManager _settingsManager;
    private readonly BroadcastProfilesManager _broadcastProfiles;

    public MiscSettingsSectionViewModel(SettingsManager settingsManager, BroadcastProfilesManager broadcastProfiles)
    {
        _settingsManager = settingsManager;
        _broadcastProfiles = broadcastProfiles;
    }

    public void Dispose()
    {
    }

    [RelayCommand]
    private void ShowAbout()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Неизвестно";

        StyledMessageBox.Show(
            $"PoproshaykaBot\n\nВерсия: {version}\n\nTwitch бот для стримеров",
            "О программе",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
            StyledMessageBox.Show(
                $"Ошибка открытия папки с настройками: {ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ResetAllSettings()
    {
        var confirmed = StyledMessageBox.Show(
            "Вы уверены, что хотите сбросить все настройки к значениям по умолчанию?\n\nЭто действие нельзя отменить.",
            "Сброс настроек",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            return;
        }

        try
        {
            _settingsManager.SaveSettings(new AppSettings());

            StyledMessageBox.Show(
                "Настройки успешно сброшены к значениям по умолчанию.\n\nПерезапустите приложение для применения изменений.",
                "Сброс настроек",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StyledMessageBox.Show($"Ошибка сброса настроек: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
            StyledMessageBox.Show(
                $"Не удалось прочитать файл: {ex.Message}",
                "Ошибка импорта",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        if (incoming is null || incoming.Count == 0)
        {
            StyledMessageBox.Show("В файле нет профилей.", "Импорт", MessageBoxButton.OK, MessageBoxImage.Information);
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

        StyledMessageBox.Show(
            $"Импортировано: {imported}. Пропущено: {skipped}.",
            "Импорт профилей",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private sealed record ExternalTwitchProfile(string? Name, string? Title, string? CategoryId);
}
