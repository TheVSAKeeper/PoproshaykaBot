﻿using System;

namespace TwitchLib.EventSub.Core.SubscriptionTypes.Channel;

/// <summary>
/// Channel Update subscription type model
/// <para>Description:</para>
/// <para>A broadcaster updates their channel properties e.g., category, title, mature flag, broadcast, or language.</para>
/// </summary>
public sealed class ChannelUpdate
{
    /// <summary>
    /// The broadcaster’s user ID.
    /// </summary>
    public string BroadcasterUserId { get; set; } = string.Empty;
    /// <summary>
    /// The broadcaster’s user display name.
    /// </summary>
    public string BroadcasterUserName { get; set; } = string.Empty;
    /// <summary>
    /// The broadcaster’s user login.
    /// </summary>
    public string BroadcasterUserLogin { get; set; } = string.Empty;
    /// <summary>
    /// The channel’s stream title.
    /// </summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// The channel’s broadcast language.
    /// </summary>
    public string Language { get; set; } = string.Empty;
    /// <summary>
    /// The channel’s category ID.
    /// </summary>
    public string CategoryId { get; set; } = string.Empty;
    /// <summary>
    /// The channel’s category name.
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;
    /// <summary>
    /// A boolean identifying whether the channel is flagged as mature.
    /// </summary>
    [Obsolete("Removed 2023‑06‑29, please use: ContentClassificationLabels")]
    public bool IsMature { get; set; }
    /// <summary>
    /// Array of content classification label IDs currently applied on the Channel.
    /// To retrieve a list of all possible IDs, use the <see href="https://dev.twitch.tv/docs/api/reference/#get-content-classification-labels">Get Content Classification Labels</see> API endpoint.
    /// </summary>
    public string[] ContentClassificationLabels { get; set; } = [];
}