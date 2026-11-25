using KeepShell.Updates;
using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using KeepUpdateApplyMode = KeepShell.Updates.UpdateApplyMode;

namespace PoproshaykaBot.Core.Update;

public static class UpdateServiceCollectionExtensions
{
    public static IServiceCollection AddSelfUpdate(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var environment = new UpdateEnvironment();
        var options = CreateOptions(environment);

        services.AddSingleton<IUpdateEnvironment>(environment);
        services.AddSingleton<IUpdateSettings, UpdateStoreSettingsAdapter>();
        services.AddKeepShellUpdates(environment, options);
        services.AddSingleFileSwapInstaller();
        services.AddSingleton<IUpdateCoordinator, UpdateCoordinator>();
        services.AddSingleton<IAppLifetimeComponent, UpdateBackgroundService>();

        return services;
    }

    internal static UpdateOptions CreateOptions(IUpdateEnvironment environment)
    {
        return new UpdateOptions
        {
            RepositorySlug = environment.RepositorySlug,
            UserAgent = "PoproshaykaBot",
            CheckInterval = TimeSpan.FromHours(6),
            ApplyMode = KeepUpdateApplyMode.Notify,
            Check = CreateCheckOptions(),
        };
    }

    internal static UpdateCheckOptions CreateCheckOptions()
    {
        return new UpdateCheckOptions
        {
            AssetNaming = new AssetNamingOptions
            {
                ProductPrefix = "PoproshaykaBot",
                PortableMarker = "-portable",
                IncludeArchitecture = true,
                Extensions = [".exe"],
            },
            ChecksumsAssetName = "SHA256SUMS-{arch}.txt",
            RuntimeAssetName = "RUNTIME-{arch}.txt",
        };
    }
}
