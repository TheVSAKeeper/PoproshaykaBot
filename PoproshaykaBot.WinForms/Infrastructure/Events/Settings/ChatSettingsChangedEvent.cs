using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Settings;

public sealed record ChatSettingsChangedEvent(ObsChatSettings Settings) : EventBase;
