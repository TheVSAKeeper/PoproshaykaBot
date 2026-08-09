using Serilog;

namespace PoproshaykaBot.Wpf.Bootstrap;

public sealed class GalleryJournal : IGalleryJournal
{
    public void Started(int cases, int themes, string directory)
    {
        Log.Information("Съёмка галереи: кейсов {Cases}, тем {Themes}, каталог {Directory}", cases, themes, directory);
    }

    public void CaseFailed(Exception exception, string caseName)
    {
        Log.Warning(exception, "Кадр {Case} не снят", caseName);
    }

    public void Finished(int frames, int skipped, long elapsedMs)
    {
        Log.Information("Съёмка галереи завершена: кадров {Frames}, пропущено {Skipped}, за {Elapsed} мс", frames, skipped, elapsedMs);
    }
}
