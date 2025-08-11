using System.Diagnostics;
using System.Reflection;

namespace PoproshaykaBot.WinForms.Settings;

public partial class MiscSettingsControl : UserControl
{
    private readonly SettingsManager _settingsManager;

    public MiscSettingsControl(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        InitializeComponent();
    }

    public event EventHandler? SettingChanged;

    public void LoadSettings(AppSettings settings)
    {
    }

    public void SaveSettings(AppSettings settings)
    {
    }

    private void OnOpenSettingsFolderButtonClicked(object sender, EventArgs e)
    {
        try
        {
            var settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PoproshaykaBot");

            if (Directory.Exists(settingsDirectory) == false)
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
            _settingsManager.SaveSettings(defaultSettings);

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
}
