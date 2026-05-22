using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Update;

public sealed class UpdateChecker(
    IGitHubReleaseClient client,
    IUpdateEnvironment environment,
    ILogger<UpdateChecker> logger)
{
    public async Task<UpdateCandidate?> CheckAsync(string? skippedVersion, CancellationToken cancellationToken)
    {
        if (environment.Kind == UpdateKind.Unsupported)
        {
            return null;
        }

        var release = await client.GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);

        if (release?.Draft != false || release.Prerelease)
        {
            return null;
        }

        if (!UpdateVersioning.TryParseTag(release.TagName, out var latest))
        {
            logger.LogWarning("Не удалось разобрать версию из тега релиза '{Tag}'", release.TagName);
            return null;
        }

        if (!UpdateVersioning.IsNewer(latest, environment.CurrentVersion))
        {
            logger.LogDebug("Установлена актуальная версия {Current} (последняя {Latest})",
                environment.CurrentVersion, latest);

            return null;
        }

        var latestText = latest.ToString();

        if (skippedVersion is not null && string.Equals(skippedVersion, latestText, StringComparison.Ordinal))
        {
            logger.LogInformation("Версия {Latest} пропущена пользователем", latestText);
            return null;
        }

        var asset = UpdateVersioning.SelectAsset(release, environment.ArchitectureMoniker, environment.Kind);

        if (asset is null)
        {
            logger.LogWarning("В релизе {Tag} нет сборки {Kind} для архитектуры {Arch}",
                release.TagName, environment.Kind, environment.ArchitectureMoniker);

            return null;
        }

        if (!HasRequiredVerificationAssets(release))
        {
            return null;
        }

        logger.LogInformation("Доступно обновление: {Latest} (текущая {Current})",
            latestText, environment.CurrentVersion);

        return new(latest, release.TagName, asset, release.HtmlUrl);
    }

    private bool HasRequiredVerificationAssets(ReleaseInfo release)
    {
        var checksumsName = UpdateVersioning.ChecksumsAssetName(environment.ArchitectureMoniker);

        if (!release.HasAsset(checksumsName))
        {
            logger.LogWarning("Релиз {Tag} не содержит файл контрольных сумм {Asset}, автоматическое обновление невозможно. "
                              + "Обновите программу вручную: {Url}",
                release.TagName, checksumsName, release.HtmlUrl);

            return false;
        }

        if (environment.Kind != UpdateKind.FrameworkDependent)
        {
            return true;
        }

        var runtimeName = UpdateVersioning.RuntimeAssetName(environment.ArchitectureMoniker);

        if (!release.HasAsset(runtimeName))
        {
            logger.LogWarning("Релиз {Tag} не содержит файл {Asset} для проверки совместимости .NET, "
                              + "автоматическое обновление невозможно. Обновите программу вручную: {Url}",
                release.TagName, runtimeName, release.HtmlUrl);

            return false;
        }

        return true;
    }
}
