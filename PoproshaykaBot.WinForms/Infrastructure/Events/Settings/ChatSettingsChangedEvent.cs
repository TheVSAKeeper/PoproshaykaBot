using PoproshaykaBot.WinForms.Settings.Obs;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Settings;

public sealed record ChatSettingsChangedEvent(ObsChatSettings Settings) : EventBase;
