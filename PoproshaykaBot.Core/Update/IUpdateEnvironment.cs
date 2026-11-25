namespace PoproshaykaBot.Core.Update;

public interface IUpdateEnvironment : KeepShell.Updates.IUpdateEnvironment
{
    new UpdateKind Kind { get; }

    string RepositorySlug { get; }
}
