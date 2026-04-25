namespace PoproshaykaBot.WinForms.Polls;

public class PollSnapshotStore
{
    private PollSnapshot? _current;

    public virtual PollSnapshot? Current => Volatile.Read(ref _current);

    public virtual void Set(PollSnapshot? snapshot)
    {
        Volatile.Write(ref _current, snapshot);
    }

    public virtual void Clear()
    {
        Volatile.Write(ref _current, null);
    }
}
