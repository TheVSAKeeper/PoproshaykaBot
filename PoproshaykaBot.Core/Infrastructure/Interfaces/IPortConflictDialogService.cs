using PoproshaykaBot.Core.Infrastructure.Http;

namespace PoproshaykaBot.Core.Infrastructure.Interfaces;

public interface IPortConflictDialogService
{
    void ShowInvalidRedirectUri(string redirectUri);

    PortConflictResolution ShowPortConflictDialog(int redirectPort, int serverPort);
}
