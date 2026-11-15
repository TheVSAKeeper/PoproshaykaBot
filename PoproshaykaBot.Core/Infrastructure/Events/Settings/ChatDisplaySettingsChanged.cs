using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.Infrastructure.Events.Settings;

public sealed record ChatDisplaySettingsChanged(string Channel, PlatformAuthRole ChatDisplayAccount) : EventBase;
