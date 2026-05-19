namespace PoproshaykaBot.Core.Update;

public static class UpdatePaths
{
    public const string StagingFolderName = "update";

    public static string StagingDirectory(string executablePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(executablePath);

        var directory = Path.GetDirectoryName(executablePath)
                        ?? throw new UpdateException("Не удалось определить каталог исполняемого файла.");

        return Path.Combine(directory, StagingFolderName);
    }
}
