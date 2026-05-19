using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Update;

public sealed class UpdateRepositoryProvider(UpdateStore store, IUpdateEnvironment environment)
    : IUpdateRepositoryProvider
{
    public string DefaultSlug => environment.RepositorySlug;

    public string Slug => UpdateRepository.Resolve(store.Load().RepositoryOverride, environment.RepositorySlug);
}
