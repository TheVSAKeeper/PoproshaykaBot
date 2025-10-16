﻿using TwitchLib.Api;
using TwitchLib.Api.Core.Interfaces;
using TwitchLib.Api.Core.Undocumented;

namespace TwitchLib.Api.Interfaces
{
    public interface ITwitchAPI
    {
        IApiSettings Settings { get; }
        Auth.Auth Auth { get; }
        Helix.Helix Helix { get; }
        ThirdParty.ThirdParty ThirdParty { get; }
        Undocumented Undocumented { get; }
    }
}
