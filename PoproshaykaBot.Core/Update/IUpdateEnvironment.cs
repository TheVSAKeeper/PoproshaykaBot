namespace PoproshaykaBot.Core.Update;

public interface IUpdateEnvironment
{
    UpdateKind Kind { get; }

    string RepositorySlug { get; }

    Version CurrentVersion { get; }

    int RuntimeMajor { get; }

    string ArchitectureMoniker { get; }

    string CurrentExecutablePath { get; }

    string StagingDirectory { get; }
}
