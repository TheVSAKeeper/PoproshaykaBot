﻿using System;
using System.Linq;
using TwitchLib.EventSub.Core.Models.Chat;

namespace TwitchLib.EventSub.Core.SubscriptionTypes.Channel
{
    /// <summary>
    /// Channel Chat Message subscription type model
    /// <para>Description:</para>
    /// <para>Any user sends a message to a channel’s chat room.</para>
    /// </summary>
    public sealed class ChannelChatMessage
    {
        /// <summary>
        /// The broadcaster user ID.
        /// </summary>
        public string BroadcasterUserId { get; set; } = string.Empty;
        /// <summary>
        /// The broadcaster display name.
        /// </summary>
        public string BroadcasterUserName { get; set; } = string.Empty;
        /// <summary>
        /// The broadcaster login.
        /// </summary>
        public string BroadcasterUserLogin { get; set; } = string.Empty;
        /// <summary>
        /// The user ID of the user that sent the message.
        /// </summary>
        public string ChatterUserId { get; set; } = string.Empty;
        /// <summary>
        /// The user name of the user that sent the message.
        /// </summary>
        public string ChatterUserName { get; set; } = string.Empty;
        /// <summary>
        /// The user login of the user that sent the message.
        /// </summary>
        public string ChatterUserLogin { get; set; } = string.Empty;
        /// <summary>
        /// A UUID that identifies the message.
        /// </summary>
        public string MessageId { get; set; } = string.Empty;
        /// <summary>
        /// The structured chat message
        /// </summary>
        public ChatMessage Message { get; set; } = new();
        /// <summary>
        /// The color of the user’s name in the chat room.
        /// </summary>
        public string Color { get; set; } = string.Empty;
        /// <summary>
        /// Array of chat badges.
        /// </summary>
        public ChatBadge[] Badges { get; set; } = [];

        /// <summary>The type of message. Possible values:</summary>
        /// <para>text</para>
        /// <para>channel_points_highlighted</para>
        /// <para>channel_points_sub_only</para>
        /// <para>user_intro</para>
        /// <para>power_ups_message_effect</para>
        /// <para>power_ups_gigantified_emote</para>
        public string MessageType { get; set; } = string.Empty;

        /// <summary>
        /// Metadata if this message is a cheer.
        /// </summary>
        public ChatCheer? Cheer { get; set; }

        /// <summary>
        /// Metadata if this message is a reply.
        /// </summary>
        public ChatReply? Reply { get; set; }

        /// <summary>
        /// Optional. The ID of a channel points custom reward that was redeemed.
        /// </summary>
        public string? ChannelPointsCustomRewardId { get; set; }

        /// <summary>
        /// Optional. The broadcaster user ID of the channel the message was sent from.
        /// </summary>
        public string? SourceBroadcasterUserId { get; set; }

        /// <summary>
        /// Optional. The user name of the broadcaster of the channel the message was sent from.
        /// </summary>
        public string? SourceBroadcasterUserName {  get; set; }

        /// <summary>
        /// Optional. The login of the broadcaster of the channel the message was sent from. 
        /// </summary>
        public string? SourceBroadcasterUserLogin {  get; set; }

        /// <summary>
        /// Optional. The UUID that identifies the source message from the channel the message was sent from.
        /// </summary>
        public string? SourceMessageId {  get; set; }

        /// <summary>
        /// Optional. The list of chat badges for the chatter in the channel the message was sent from. 
        /// </summary>
        public ChatBadge[]? SourceBadges { get; set; }

        /// <summary>
        /// Optional. Determines if a message delivered during a shared chat session is only sent to the source channel.
        /// Has no effect if the message is not sent during a shared chat session.
        /// </summary>
        public bool? IsSourceOnly { get; set; }
      
        /// <summary>
        /// Returns true if viewer is a subscriber
        /// </summary>
        public bool IsSubscriber => Badges.Any(x => x.SetId.Equals("subscriber", StringComparison.OrdinalIgnoreCase) || x.SetId.Equals("founder", StringComparison.OrdinalIgnoreCase));
        /// <summary>
        /// Returns true if viewer is a moderator
        /// </summary>
        public bool IsModerator => Badges.Any(x => x.SetId.Equals("moderator", StringComparison.OrdinalIgnoreCase));
        /// <summary>
        /// Returns true if viewer is a broadcaster
        /// </summary>
        public bool IsBroadcaster => Badges.Any(x => x.SetId.Equals("broadcaster", StringComparison.OrdinalIgnoreCase));
        /// <summary>
        /// Returns true if viewer is a vip
        /// </summary>
        public bool IsVip => Badges.Any(x => x.SetId.Equals("vip", StringComparison.OrdinalIgnoreCase));
        /// <summary>
        /// Returns true if viewer is a staff member
        /// </summary>
        public bool IsStaff => Badges.Any(x => x.SetId.Equals("staff", StringComparison.OrdinalIgnoreCase) || x.SetId.Equals("admin", StringComparison.OrdinalIgnoreCase));
    }
}
