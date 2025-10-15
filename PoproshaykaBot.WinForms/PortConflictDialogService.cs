using PoproshaykaBot.Core;
using PoproshaykaBot.Core.Infrastructure.Http;
using PoproshaykaBot.Core.Infrastructure.Interfaces;

namespace PoproshaykaBot.WinForms;

public sealed class PortConflictDialogService : IPortConflictDialogService
{
    public void ShowInvalidRedirectUri(string redirectUri)
    {
        MessageBox.Show($"Некорректный RedirectUri: {redirectUri}\n\nПожалуйста, исправьте URI в настройках OAuth.",
            "Ошибка конфигурации",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    public PortConflictResolution ShowPortConflictDialog(int redirectPort, int serverPort)
    {
        var message = $"""
                       Обнаружен конфликт портов:

                       • RedirectUri использует порт: {redirectPort}
                       • HTTP сервер настроен на порт: {serverPort}

                       Для корректной работы OAuth авторизации и HTTP сервера необходимо использовать один и тот же порт.

                       Нажмите:
                       • ДА - использовать порт {redirectPort} для HTTP сервера
                       • НЕТ - обновить RedirectUri на порт {serverPort}
                       • ОТМЕНА - открыть настройки для ручного исправления
                       """;

        var result = MessageBox.Show(message,
            "Конфликт портов",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button1);

        return result switch
        {
            DialogResult.Yes => PortConflictResolution.UseRedirectPort,
            DialogResult.No => PortConflictResolution.UpdateRedirectUri,
            _ => PortConflictResolution.OpenSettings,
        };
    }
}
