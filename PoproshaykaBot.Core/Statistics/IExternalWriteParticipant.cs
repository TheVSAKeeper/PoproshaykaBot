namespace PoproshaykaBot.Core.Statistics;

public interface IExternalWriteParticipant
{
    IReadOnlyCollection<string> OwnedRelativePaths { get; }

    ValueTask<bool> InvalidateAsync(CancellationToken cancellationToken);

    ValueTask RestoreAsync(bool wasLoaded);

    void LatchExternalRewrite();
}
