namespace PoproshaykaBot.WinForms.Tests.Polls;

internal sealed class TestTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow()
    {
        return UtcNow;
    }
}
