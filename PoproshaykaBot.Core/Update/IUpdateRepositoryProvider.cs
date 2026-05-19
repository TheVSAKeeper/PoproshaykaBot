namespace PoproshaykaBot.Core.Update;

public interface IUpdateRepositoryProvider
{
    string DefaultSlug { get; }

    string Slug { get; }
}
