using KeepShell.Updates;
using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Hosting;

namespace PoproshaykaBot.Core.Update;

public static class UpdateServiceCollectionExtensions
{
    public const string DefaultRepositorySlug = "MaxNagibator/PoproshaykaBot";

    public static IServiceCollection AddSelfUpdate(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var environment = UpdateEnvironment.FromEntryAssembly();
        var options = CreateOptions(UpdateEnvironment.ResolveRepositorySlug(DefaultRepositorySlug));

        services.AddSingleton<IUpdateSettings, UpdateStoreSettingsAdapter>();
        services.AddKeepShellUpdates(environment, options, UpdateDeliveryMode.SingleFileSwap);
        services.AddSingleton<IAppLifetimeComponent, UpdateBackgroundService>();

        return services;
    }

    internal static UpdateOptions CreateOptions(string repositorySlug = DefaultRepositorySlug)
    {
        return new UpdateOptions
        {
            RepositorySlug = repositorySlug,
            UserAgent = "PoproshaykaBot",
            CheckInterval = TimeSpan.FromHours(6),
            ApplyMode = UpdateApplyMode.Notify,
            Check = CreateCheckOptions(),
        };
    }

    internal static UpdateCheckOptions CreateCheckOptions()
    {
        return new UpdateCheckOptions
        {
            AssetNaming = new AssetNamingOptions
            {
                ProductPrefix = "PoproshaykaBot-v",
                PortableMarker = "-portable",
                IncludeArchitecture = true,
                Extensions = [".zip", ".exe"],
            },
            ChecksumsAssetName = "SHA256SUMS-{arch}.txt",
            RuntimeAssetName = "RUNTIME-{arch}.txt",
        };
    }
}
