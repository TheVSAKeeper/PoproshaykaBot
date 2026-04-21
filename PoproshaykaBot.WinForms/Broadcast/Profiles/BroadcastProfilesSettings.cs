namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class BroadcastProfilesSettings
{
    public List<BroadcastProfile> Profiles { get; set; } = [];
    public Guid? LastAppliedProfileId { get; set; }
}
