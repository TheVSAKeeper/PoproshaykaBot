using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Settings;

public partial class MiscSettingsControl : UserControl
{
    private static readonly JsonSerializerOptions ImportJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public MiscSettingsControl()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingChanged;

    [Inject]
    public SettingsManager SettingsManager { get; internal init; } = null!;

    [Inject]
    public BroadcastProfilesManager BroadcastProfiles { get; internal init; } = null!;

    public void LoadSettings(AppSettings settings)
    {
        // Состояния нет, метод существует ради единообразия с другими *SettingsControl в SettingsForm.LoadSettingsToControls.
    }

    public void SaveSettings(AppSettings settings)
    {
        // Сохранять нечего, см. LoadSettings.
    }

    private void OnOpenSettingsFolderButtonClicked(object sender, EventArgs e)
    {
        try
        {
            var settingsDirectory = AppPaths.BaseDirectory;

            if (!Directory.Exists(settingsDirectory))
            {
                Directory.CreateDirectory(settingsDirectory);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = settingsDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Ошибка открытия папки с настройками: {exception.Message}", "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnResetAllSettingsButtonClicked(object sender, EventArgs e)
    {
        var result = MessageBox.Show("Вы уверены, что хотите сбросить все настройки к значениям по умолчанию?\n\nЭто действие нельзя отменить.",
            "Сброс настроек",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var defaultSettings = new AppSettings();
            SettingsManager.SaveSettings(defaultSettings);

            MessageBox.Show("Настройки успешно сброшены к значениям по умолчанию.\n\nПерезапустите приложение для применения изменений.",
                "Сброс настроек", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Ошибка сброса настроек: {exception.Message}", "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnAboutButtonClicked(object sender, EventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Неизвестно";
        var aboutText = $"PoproshaykaBot\n\nВерсия: {version}\n\nTwitch бот для стримеров";

        MessageBox.Show(aboutText, "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnImportBroadcastProfilesButtonClicked(object sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Импорт профилей трансляций из JSON",
            Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
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
            MessageBox.Show(this, $"Не удалось прочитать файл: {ex.Message}",
                "Ошибка импорта", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return;
        }

        if (incoming == null || incoming.Count == 0)
        {
            MessageBox.Show(this, "В файле нет профилей.",
                "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return;
        }

        var imported = 0;
        var skipped = 0;
        var existingNames = new HashSet<string>(BroadcastProfiles.GetAll().Select(p => p.Name),
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
                BroadcastProfiles.Upsert(profile);
                existingNames.Add(name);
                imported++;
            }
            catch (InvalidOperationException)
            {
                skipped++;
            }
        }

        MessageBox.Show(this,
            $"Импортировано: {imported}. Пропущено: {skipped}.",
            "Импорт профилей", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private sealed record ExternalTwitchProfile(string? Name, string? Title, string? CategoryId);
}
