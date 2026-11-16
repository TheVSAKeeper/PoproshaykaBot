// Copyright (c) 2025 Agash Thamo. Licensed under the MIT License. See THIRD-PARTY-NOTICES.txt.

using System.Globalization;

namespace PoproshaykaBot.Core.YouTube.Chat.InnerTube;

internal static class InnerTubeColorConverter
{
    public static string ToHex6Color(this long value)
    {
        var rgb = unchecked((uint)value) & 0xFFFFFFu;
        return rgb.ToString("X6", CultureInfo.InvariantCulture);
    }
}
