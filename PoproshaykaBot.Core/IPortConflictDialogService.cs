namespace PoproshaykaBot.Core;

public interface IPortConflictDialogService
{
    void ShowInvalidRedirectUri(string redirectUri);

    PortConflictResolution ShowPortConflictDialog(int redirectPort, int serverPort);
}
