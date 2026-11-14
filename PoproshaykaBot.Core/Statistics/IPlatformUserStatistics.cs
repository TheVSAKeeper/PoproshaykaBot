using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

public interface IPlatformUserStatistics
{
    StreamingPlatform Platform { get; }

    UserStatistics? GetById(string userId);

    UserStatistics? GetByName(string username);

    IReadOnlyList<UserStatistics> GetAll();

    IReadOnlyList<UserStatistics> GetTop(int count, UserTopMode mode = UserTopMode.Points);
}
