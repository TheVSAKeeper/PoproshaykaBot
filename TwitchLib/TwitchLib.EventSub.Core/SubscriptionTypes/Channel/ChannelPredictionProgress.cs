﻿using System;
using TwitchLib.EventSub.Core.Models.Predictions;

namespace TwitchLib.EventSub.Core.SubscriptionTypes.Channel;

/// <summary>
/// Channel Prediction Progress subscription type model
/// <para>Description:</para>
/// <para>Users participated in a Prediction on a specified channel.</para>
/// </summary>
public sealed class ChannelPredictionProgress : ChannelPredictionBase
{
    /// <summary>
    /// The time the Channel Points Prediction will automatically lock.
    /// </summary>
    public DateTimeOffset LocksAt { get; set; } = DateTimeOffset.MinValue;
}