using System.IO;
using System.Text;

namespace PoproshaykaBot.Wpf.Tests;

internal static class GalleryTestProfile
{
    private const string SettingsJson = """
                                        {
                                          "twitch": {
                                            "channel": "gallery-test",
                                            "clientId": "gallery-test-client-id",
                                            "clientSecret": "gallery-test-client-secret"
                                          }
                                        }
                                        """;

    private const string BroadcastProfilesJson = """
                                                 {
                                                   "profiles": [
                                                     {
                                                       "id": "3f2b0f0e-4d1f-4a4a-9c1a-9f1f6f0a1b2c",
                                                       "name": "Вечерний стрим",
                                                       "title": "Играем и общаемся",
                                                       "gameId": "509658",
                                                       "gameName": "Just Chatting",
                                                       "broadcasterLanguage": "ru",
                                                       "tags": ["Русский", "Общение"],
                                                       "obsSceneName": "Основная",
                                                       "currentNumber": 42
                                                     }
                                                   ]
                                                 }
                                                 """;

    private const string BlockersText = """
                                        # блокировка баннеров
                                        [data-a-target="community-highlight"]
                                        .stream-chat-header
                                        """;

    public static string Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PoproshaykaBot-gallery-test-" + Guid.NewGuid().ToString("N"));
        var settingsDirectory = Path.Combine(directory, "settings");

        Directory.CreateDirectory(settingsDirectory);

        Write(Path.Combine(settingsDirectory, "settings.json"), SettingsJson);
        Write(Path.Combine(settingsDirectory, "broadcast-profiles.json"), BroadcastProfilesJson);
        Write(Path.Combine(directory, "chat-blockers.txt"), BlockersText);

        return directory;
    }

    public static void Delete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Write(string path, string content)
    {
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
