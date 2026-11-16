// Copyright (c) 2025 Agash Thamo. Licensed under the MIT License. See THIRD-PARTY-NOTICES.txt.

using PoproshaykaBot.Core.YouTube.Chat.InnerTube.Models;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PoproshaykaBot.Core.YouTube.Chat.InnerTube;

internal enum InnerTubeContinuationKind
{
    None = 0,
    Active = 1,
    Reload = 2,
    Ended = 3,
}

internal sealed record InnerTubeBootstrapResult(
    string VideoId,
    string ApiKey,
    string ClientVersion,
    string InitialContinuation);

internal sealed record InnerTubeParsedBatch(
    IReadOnlyList<YouTubeLiveChatItem> Items,
    string? NextContinuation,
    TimeSpan PollingInterval,
    InnerTubeContinuationKind ContinuationKind,
    string? LiveChatSwitchContinuation);

internal static partial class InnerTubeLiveChatParser
{
    private static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinPollingInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxPollingInterval = TimeSpan.FromSeconds(30);

    public static InnerTubeBootstrapResult ParseWatchBootstrap(string html, string requestedVideoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedVideoId);

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.ParseFailure,
                "Страница трансляции InnerTube вернула пустой HTML.");
        }

        if (IsConsentPage(html))
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.ConsentRequired,
                "YouTube потребовал подтверждение consent перед доступом к странице трансляции.");
        }

        var inaccessibleReason = DetectInaccessibleLiveReason(html);

        if (string.Equals(inaccessibleReason, "members-only", StringComparison.Ordinal))
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.MembersOnly,
                "Трансляция доступна только спонсорам канала.");
        }

        if (string.Equals(inaccessibleReason, "login-required", StringComparison.Ordinal))
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.LoginRequired,
                "Для просмотра трансляции через InnerTube требуется вход в аккаунт.");
        }

        if (inaccessibleReason is not null)
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.StreamUnavailable,
                $"Трансляция недоступна для InnerTube ({inaccessibleReason}).");
        }

        if (ReplayRegex().IsMatch(html))
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.LiveStreamEnded,
                "Трансляция уже перешла в режим повтора (isReplay).");
        }

        var apiKey = ExtractGroup(ApiKeyRegex().Match(html));
        var clientVersion = ExtractGroup(ClientVersionRegex().Match(html));
        var continuation = ExtractGroup(ContinuationRegex().Match(html));

        if (string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(clientVersion)
            || string.IsNullOrWhiteSpace(continuation))
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.ParseFailure,
                "На странице трансляции не найдены обязательные параметры InnerTube (apiKey/clientVersion/continuation).");
        }

        var resolvedVideoId = ExtractVideoId(html) ?? requestedVideoId;
        return new(resolvedVideoId, apiKey, clientVersion, continuation);
    }

    public static InnerTubeParsedBatch ParseBatch(LiveChatResponse? response, bool retainRawPayloads = false)
    {
        var lcc = response?.ContinuationContents?.LiveChatContinuation;

        if (lcc is null)
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.ParseFailure,
                "Ответ InnerTube get_live_chat не содержит continuationContents.liveChatContinuation.");
        }

        var liveChatSwitchContinuation = ExtractLiveChatSwitchContinuation(lcc);
        var (contKind, nextToken, pollingInterval) = ExtractContinuation(response!, lcc);

        var items = new List<YouTubeLiveChatItem>(lcc.Actions?.Count ?? 0);

        if (lcc.Actions is not null)
        {
            for (var i = 0; i < lcc.Actions.Count; i++)
            {
                var action = lcc.Actions[i];

                if (action is null || action.AddLiveChatTickerItemAction is not null)
                {
                    continue;
                }

                var rawPayloadJson = retainRawPayloads
                    ? JsonSerializer.Serialize(action, InnerTubeJsonSerializerContext.Default.ContinuationAction)
                    : null;

                var mapped = MapAction(action, i, rawPayloadJson);

                if (mapped is not null)
                {
                    items.Add(mapped);
                }
            }
        }

        if (contKind == InnerTubeContinuationKind.Ended && !ContainsChatEnded(items))
        {
            items.Add(new(
                "innertube-chat-ended",
                YouTubeLiveChatItemKind.ChatEnded,
                PublishedAtUtc: null,
                string.Empty,
                string.Empty,
                EmptyAuthor(),
                "liveChatReplayContinuationData"));
        }

        return new(items, nextToken, pollingInterval, contKind, liveChatSwitchContinuation);
    }

    internal static bool IsConsentPage(string html)
    {
        return html.Contains("consent.youtube.com", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Before you continue to YouTube", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? DetectInaccessibleLiveReason(string html)
    {
        var hasUnplayableStatus =
            html.Contains("\"playabilityStatus\":{\"status\":\"UNPLAYABLE\"", StringComparison.Ordinal)
            || html.Contains("\"status\":\"UNPLAYABLE\"", StringComparison.Ordinal);

        var hasMembersOnlyMarker =
            html.Contains("playerLegacyDesktopYpcOfferRenderer", StringComparison.OrdinalIgnoreCase)
            || html.Contains("BADGE_STYLE_TYPE_MEMBERS_ONLY", StringComparison.OrdinalIgnoreCase)
            || html.Contains("SPONSORSHIP_STAR", StringComparison.OrdinalIgnoreCase);

        if (hasUnplayableStatus && hasMembersOnlyMarker)
        {
            return "members-only";
        }

        if (html.Contains("\"status\":\"LOGIN_REQUIRED\"", StringComparison.Ordinal))
        {
            return "login-required";
        }

        if (hasUnplayableStatus)
        {
            return "unplayable";
        }

        return html.Contains("\"status\":\"ERROR\"", StringComparison.Ordinal)
            ? "error"
            : null;
    }

    private static string? ExtractLiveChatSwitchContinuation(LiveChatContinuation lcc)
    {
        var subMenuItems = lcc.Header?.LiveChatHeaderRenderer?.ViewSelector?.SortFilterSubMenuRenderer?.SubMenuItems;

        if (subMenuItems is [_, var secondItem, ..]
            && !secondItem.Selected
            && !string.IsNullOrWhiteSpace(secondItem.Continuation?.ReloadContinuationData?.Continuation))
        {
            return secondItem.Continuation.ReloadContinuationData.Continuation;
        }

        return null;
    }

    private static (InnerTubeContinuationKind Kind, string? Continuation, TimeSpan PollingInterval) ExtractContinuation(
        LiveChatResponse response,
        LiveChatContinuation lcc)
    {
        if (lcc.Continuations is { Count: > 0 })
        {
            foreach (var cont in lcc.Continuations)
            {
                if (cont.InvalidationContinuationData is { Continuation: { Length: > 0 } invToken } inv
                    && !string.IsNullOrWhiteSpace(invToken))
                {
                    return (InnerTubeContinuationKind.Active, invToken, ClampTimeoutMs(inv.TimeoutMs));
                }

                if (cont.TimedContinuationData is { Continuation: { Length: > 0 } timedToken } timed
                    && !string.IsNullOrWhiteSpace(timedToken))
                {
                    return (InnerTubeContinuationKind.Active, timedToken, ClampTimeoutMs(timed.TimeoutMs));
                }

                if (cont.ReloadContinuationData is { Continuation: { Length: > 0 } reloadToken }
                    && !string.IsNullOrWhiteSpace(reloadToken))
                {
                    return (InnerTubeContinuationKind.Reload, reloadToken, DefaultPollingInterval);
                }

                if (cont.LiveChatReplayContinuationData is not null)
                {
                    return (InnerTubeContinuationKind.Ended, null, DefaultPollingInterval);
                }
            }
        }

        if (response.InvalidationContinuationData is { Continuation: { Length: > 0 } rootToken } rootInv
            && !string.IsNullOrWhiteSpace(rootToken))
        {
            return (InnerTubeContinuationKind.Active, rootToken, ClampTimeoutMs(rootInv.TimeoutMs));
        }

        return (InnerTubeContinuationKind.Ended, null, DefaultPollingInterval);
    }

    private static TimeSpan ClampTimeoutMs(int timeoutMs)
    {
        if (timeoutMs <= 0)
        {
            return DefaultPollingInterval;
        }

        var clampedMs = Math.Clamp(timeoutMs, (int)MinPollingInterval.TotalMilliseconds, (int)MaxPollingInterval.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(clampedMs);
    }

    private static YouTubeLiveChatItem? MapAction(ContinuationAction action, int actionIndex, string? rawPayloadJson)
    {
        if (action.AddChatItemAction?.Item is { } addItem)
        {
            return MapAddChatItem(addItem, rawPayloadJson);
        }

        if (action.RemoveChatItemAction is { TargetItemId: { Length: > 0 } deletedId }
            && !string.IsNullOrWhiteSpace(deletedId))
        {
            return new(
                $"mod-del:{deletedId}",
                YouTubeLiveChatItemKind.Tombstone,
                PublishedAtUtc: null,
                string.Empty,
                string.Empty,
                EmptyAuthor(),
                "removeChatItemAction",
                Moderation: new(YouTubeModerationActionKind.MessageDeleted, TargetMessageId: deletedId),
                RawPayloadJson: rawPayloadJson);
        }

        if (action.MarkChatItemAsDeletedAction is { TargetItemId: { Length: > 0 } markedDeletedId }
            && !string.IsNullOrWhiteSpace(markedDeletedId))
        {
            return new(
                $"mod-del:{markedDeletedId}",
                YouTubeLiveChatItemKind.Tombstone,
                PublishedAtUtc: null,
                string.Empty,
                string.Empty,
                EmptyAuthor(),
                "markChatItemAsDeletedAction",
                Moderation: new(YouTubeModerationActionKind.MessageDeleted, TargetMessageId: markedDeletedId),
                RawPayloadJson: rawPayloadJson);
        }

        var authorChannelId = action.RemoveChatItemByAuthorAction?.ExternalChannelId
            ?? action.MarkChatItemsByAuthorAsDeletedAction?.ExternalChannelId;

        if (!string.IsNullOrWhiteSpace(authorChannelId))
        {
            var rawType = action.RemoveChatItemByAuthorAction is not null
                ? "removeChatItemByAuthorAction"
                : "markChatItemsByAuthorAsDeletedAction";

            return new(
                $"mod-author:{authorChannelId}:{actionIndex}",
                YouTubeLiveChatItemKind.UserBanned,
                PublishedAtUtc: null,
                string.Empty,
                string.Empty,
                EmptyAuthor(),
                rawType,
                Moderation: new(YouTubeModerationActionKind.AuthorMessagesDeleted, TargetChannelId: authorChannelId),
                RawPayloadJson: rawPayloadJson);
        }

        if (action.ReplaceChatItemAction is { TargetItemId: { Length: > 0 } replaceTargetId } replaceAction
            && !string.IsNullOrWhiteSpace(replaceTargetId))
        {
            YouTubeLiveChatItem? replacement = null;

            if (replaceAction.ReplacementItem?.LiveChatTextMessageRenderer is { } replacementText)
            {
                replacement = MapAddChatItem(
                    new AddChatItemActionItem { LiveChatTextMessageRenderer = replacementText },
                    rawPayloadJson: null);
            }

            return new(
                $"mod-replace:{replaceTargetId}",
                YouTubeLiveChatItemKind.Other,
                PublishedAtUtc: null,
                string.Empty,
                string.Empty,
                EmptyAuthor(),
                "replaceChatItemAction",
                Moderation: new(
                    YouTubeModerationActionKind.MessageReplaced,
                    TargetMessageId: replaceTargetId,
                    ReplacementItem: replacement),
                RawPayloadJson: rawPayloadJson);
        }

        return null;
    }

    private static YouTubeLiveChatItem? MapAddChatItem(AddChatItemActionItem item, string? rawPayloadJson)
    {
        if (item.GiftMessageViewModel is { } giftVm)
        {
            return MapGiftViewModel(giftVm, rawPayloadJson);
        }

        if (item.LiveChatViewerEngagementMessageRenderer is { } engagement)
        {
            return MapEngagementMessage(engagement, rawPayloadJson);
        }

        MessageRendererBase? baseRenderer = item switch
        {
            { LiveChatTextMessageRenderer: not null } => item.LiveChatTextMessageRenderer,
            { LiveChatPaidMessageRenderer: not null } => item.LiveChatPaidMessageRenderer,
            { LiveChatPaidStickerRenderer: not null } => item.LiveChatPaidStickerRenderer,
            { LiveChatMembershipItemRenderer: not null } => item.LiveChatMembershipItemRenderer,
            { LiveChatSponsorshipsGiftPurchaseAnnouncementRenderer: not null } =>
                item.LiveChatSponsorshipsGiftPurchaseAnnouncementRenderer,
            { LiveChatSponsorshipsGiftRedemptionAnnouncementRenderer: not null } =>
                item.LiveChatSponsorshipsGiftRedemptionAnnouncementRenderer,
            _ => null,
        };

        if (baseRenderer is null || string.IsNullOrWhiteSpace(baseRenderer.Id))
        {
            return null;
        }

        var (author, authorBadgeLabel) = BuildAuthor(baseRenderer);
        var publishedAtUtc = TryParseTimestampUsec(baseRenderer.TimestampUsec);

        if (item.LiveChatTextMessageRenderer is { } textRenderer)
        {
            var (runs, plainText) = MapMessageRuns(textRenderer.Message?.Runs);

            return new(
                baseRenderer.Id,
                YouTubeLiveChatItemKind.TextMessage,
                publishedAtUtc,
                plainText,
                plainText,
                author,
                "liveChatTextMessageRenderer",
                Runs: runs,
                RawPayloadJson: rawPayloadJson);
        }

        if (item.LiveChatPaidMessageRenderer is { } paidRenderer)
        {
            var (runs, plainText) = MapMessageRuns(paidRenderer.Message?.Runs);
            var amountString = paidRenderer.PurchaseAmountText?.Text ?? string.Empty;
            var (amountValue, currency) = CurrencyParser.Parse(amountString);
            var monetary = new YouTubeMonetaryDetails(
                AmountMicros: null,
                amountValue,
                currency,
                amountString,
                HeaderBackgroundColorHex: paidRenderer.HeaderBackgroundColor.ToHex6Color(),
                BodyBackgroundColorHex: paidRenderer.BodyBackgroundColor.ToHex6Color());

            return new(
                baseRenderer.Id,
                YouTubeLiveChatItemKind.SuperChat,
                publishedAtUtc,
                plainText,
                string.IsNullOrEmpty(plainText) ? amountString : $"{amountString}: {plainText}",
                author,
                "liveChatPaidMessageRenderer",
                Runs: runs,
                Monetary: monetary,
                RawPayloadJson: rawPayloadJson);
        }

        if (item.LiveChatPaidStickerRenderer is { } stickerRenderer)
        {
            var amountString = stickerRenderer.PurchaseAmountText?.Text ?? string.Empty;
            var (amountValue, currency) = CurrencyParser.Parse(amountString);
            var stickerAlt = stickerRenderer.Sticker?.Accessibility?.AccessibilityData?.Label
                ?? stickerRenderer.Sticker?.Accessibility?.Label;
            var stickerUrl = GetLastThumbnailUrl(stickerRenderer.Sticker?.Thumbnails);

            var monetary = new YouTubeMonetaryDetails(
                AmountMicros: null,
                amountValue,
                currency,
                amountString,
                BodyBackgroundColorHex: stickerRenderer.BackgroundColor.ToHex6Color(),
                StickerAltText: stickerAlt,
                StickerImageUrl: stickerUrl);

            return new(
                baseRenderer.Id,
                YouTubeLiveChatItemKind.SuperSticker,
                publishedAtUtc,
                stickerAlt ?? string.Empty,
                string.IsNullOrEmpty(stickerAlt) ? amountString : $"{amountString} {stickerAlt}",
                author,
                "liveChatPaidStickerRenderer",
                Monetary: monetary,
                RawPayloadJson: rawPayloadJson);
        }

        if (item.LiveChatMembershipItemRenderer is { } membershipRenderer)
        {
            var membership = MapMembershipItem(membershipRenderer, out var runs, out var plainText, out var displayText);

            return new(
                baseRenderer.Id,
                membership.EventKind == YouTubeMembershipEventKind.Milestone
                    ? YouTubeLiveChatItemKind.MemberMilestoneChat
                    : YouTubeLiveChatItemKind.NewSponsor,
                publishedAtUtc,
                plainText,
                displayText,
                author with { IsChatSponsor = true, MembershipBadgeLabel = authorBadgeLabel ?? author.MembershipBadgeLabel },
                "liveChatMembershipItemRenderer",
                Runs: runs,
                Membership: membership,
                RawPayloadJson: rawPayloadJson);
        }

        if (item.LiveChatSponsorshipsGiftPurchaseAnnouncementRenderer is { } giftPurchase)
        {
            var gifterHeader = giftPurchase.Header?.LiveChatSponsorshipsHeaderRenderer;
            var (effectiveAuthor, membership) = MapGiftPurchase(author, gifterHeader);

            return new(
                baseRenderer.Id,
                YouTubeLiveChatItemKind.MembershipGifting,
                publishedAtUtc,
                string.Empty,
                membership.HeaderText ?? string.Empty,
                effectiveAuthor,
                "liveChatSponsorshipsGiftPurchaseAnnouncementRenderer",
                Membership: membership,
                RawPayloadJson: rawPayloadJson);
        }

        if (item.LiveChatSponsorshipsGiftRedemptionAnnouncementRenderer is { } giftRedemption)
        {
            var (runs, plainText) = MapMessageRuns(giftRedemption.Message?.Runs);
            var membership = MapGiftRedemption(giftRedemption, plainText);

            return new(
                baseRenderer.Id,
                YouTubeLiveChatItemKind.GiftMembershipReceived,
                publishedAtUtc,
                plainText,
                plainText,
                author with { IsChatSponsor = true },
                "liveChatSponsorshipsGiftRedemptionAnnouncementRenderer",
                Runs: runs,
                Membership: membership,
                RawPayloadJson: rawPayloadJson);
        }

        return null;
    }

    private static YouTubeLiveChatItem? MapGiftViewModel(GiftMessageViewModel vm, string? rawPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(vm.Id))
        {
            return null;
        }

        var authorName = vm.AuthorName?.Content?.Trim() ?? string.Empty;
        var text = vm.Text?.Content ?? string.Empty;
        var avatarUrl = vm.AuthorAvatar?.AvatarViewModel?.Image?.Sources is [.., var lastAvatarSource]
            ? lastAvatarSource.Url
            : null;
        var giftImageUrl = vm.GiftImage?.Sources is [.., var lastGiftSource]
            ? lastGiftSource.Url
            : null;

        int? jewelAmount = null;
        var giftMatch = JewelsGiftTextRegex().Match(text);

        if (giftMatch.Success && int.TryParse(giftMatch.Groups["jewels"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedJewels))
        {
            jewelAmount = parsedJewels;
        }

        var author = new YouTubeLiveChatAuthor(
            string.Empty,
            authorName,
            IsChatOwner: false,
            IsChatModerator: false,
            IsChatSponsor: false,
            IsVerified: false,
            AvatarUrl: avatarUrl,
            Handle: authorName.StartsWith('@') ? authorName : null);

        var monetary = new YouTubeMonetaryDetails(
            AmountMicros: null,
            AmountValue: null,
            Currency: null,
            AmountDisplayString: text,
            StickerAltText: vm.GiftImageA11yLabel,
            StickerImageUrl: giftImageUrl,
            JewelsAmount: jewelAmount);

        return new(
            vm.Id,
            YouTubeLiveChatItemKind.Gift,
            PublishedAtUtc: null,
            text,
            text,
            author,
            "giftMessageViewModel",
            Monetary: monetary,
            RawPayloadJson: rawPayloadJson);
    }

    private static YouTubeLiveChatItem? MapEngagementMessage(
        LiveChatViewerEngagementMessageRenderer renderer,
        string? rawPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(renderer.Id))
        {
            return null;
        }

        var (runs, plainText) = MapMessageRuns(renderer.Message?.Runs);

        return new(
            renderer.Id,
            YouTubeLiveChatItemKind.Other,
            TryParseTimestampUsec(renderer.TimestampUsec),
            plainText,
            plainText,
            EmptyAuthor(),
            "liveChatViewerEngagementMessageRenderer",
            Runs: runs,
            RawPayloadJson: rawPayloadJson);
    }

    private static YouTubeMembershipDetails MapMembershipItem(
        LiveChatMembershipItemRenderer item,
        out IReadOnlyList<YouTubeChatRun> runs,
        out string plainText,
        out string displayText)
    {
        var runsSource = item.Message?.Runs ?? item.HeaderSubtext?.Runs;
        (runs, plainText) = MapMessageRuns(runsSource);

        var headerPrimaryText = ConcatenateRunsText(item.HeaderPrimaryText?.Runs) ?? item.HeaderPrimaryText?.SimpleText;
        var headerSubtext = !string.IsNullOrEmpty(item.HeaderSubtext?.SimpleText)
            ? item.HeaderSubtext.SimpleText
            : ConcatenateRunsText(item.HeaderSubtext?.Runs);

        displayText = !string.IsNullOrEmpty(plainText)
            ? plainText
            : headerPrimaryText ?? headerSubtext ?? string.Empty;

        var badgeLabel = ExtractMembershipBadgeLabel(item.AuthorBadges);
        var eventKind = YouTubeMembershipEventKind.NewMember;
        string? levelName = null;
        int? milestoneMonths = null;

        var badgeIndicatesNew = badgeLabel?.Contains("new member", StringComparison.OrdinalIgnoreCase) == true;

        if (headerSubtext?.StartsWith("Upgraded membership to", StringComparison.OrdinalIgnoreCase) == true)
        {
            eventKind = YouTubeMembershipEventKind.Upgraded;
            levelName = TryExtractTierNameFromRuns(item.HeaderSubtext?.Runs);

            if (string.IsNullOrWhiteSpace(levelName) && !string.IsNullOrEmpty(headerSubtext))
            {
                var match = UpgradedMemberLevelFromSubtextRegex().Match(headerSubtext);

                if (match.Success)
                {
                    levelName = match.Groups["level"].Value.Trim();
                }
            }
        }
        else if (headerPrimaryText?.Contains("member for", StringComparison.OrdinalIgnoreCase) == true)
        {
            eventKind = YouTubeMembershipEventKind.Milestone;
            levelName = string.IsNullOrWhiteSpace(headerSubtext) ? null : headerSubtext.Trim();

            var monthsMatch = MilestoneMonthsRegex().Match(headerPrimaryText);

            if (monthsMatch.Success && int.TryParse(monthsMatch.Groups["months"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMonths))
            {
                milestoneMonths = parsedMonths;
            }
        }
        else if (headerPrimaryText?.StartsWith("Welcome", StringComparison.OrdinalIgnoreCase) == true
            || headerSubtext?.StartsWith("New member", StringComparison.OrdinalIgnoreCase) == true
            || headerSubtext?.StartsWith("Welcome to", StringComparison.OrdinalIgnoreCase) == true
            || badgeIndicatesNew)
        {
            eventKind = YouTubeMembershipEventKind.NewMember;
            levelName = TryExtractTierNameFromRuns(item.HeaderSubtext?.Runs);

            if (string.IsNullOrWhiteSpace(levelName) && !string.IsNullOrEmpty(headerSubtext))
            {
                var subtextMatch = NewMemberLevelFromSubtextRegex().Match(headerSubtext);

                if (subtextMatch.Success)
                {
                    levelName = subtextMatch.Groups["level"].Value.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(levelName) && !string.IsNullOrEmpty(headerPrimaryText))
            {
                var primaryMatch = NewMemberLevelRegex().Match(headerPrimaryText);

                if (primaryMatch.Success)
                {
                    levelName = primaryMatch.Groups["level"].Value.Trim();
                }
            }
        }

        return new(
            eventKind,
            LevelName: levelName,
            MilestoneMonths: milestoneMonths,
            HeaderText: headerPrimaryText ?? headerSubtext);
    }

    private static (YouTubeLiveChatAuthor Author, YouTubeMembershipDetails Membership) MapGiftPurchase(
        YouTubeLiveChatAuthor fallbackAuthor,
        LiveChatSponsorshipsHeaderRenderer? gifterHeader)
    {
        var effectiveAuthor = fallbackAuthor;

        if (gifterHeader is not null)
        {
            var gifterName = gifterHeader.AuthorName?.Text ?? fallbackAuthor.DisplayName;
            var gifterAvatar = GetLastThumbnailUrl(gifterHeader.AuthorPhoto?.Thumbnails) ?? fallbackAuthor.AvatarUrl;
            ParseBadges(
                gifterHeader.AuthorBadges,
                out var isOwner,
                out var isModerator,
                out var isSponsor,
                out var isVerified,
                out var badgeLabel);

            effectiveAuthor = fallbackAuthor with
            {
                DisplayName = gifterName,
                AvatarUrl = gifterAvatar,
                IsChatOwner = isOwner,
                IsChatModerator = isModerator,
                IsChatSponsor = isSponsor,
                IsVerified = isVerified,
                MembershipBadgeLabel = badgeLabel ?? fallbackAuthor.MembershipBadgeLabel,
            };
        }

        var headerPrimaryText = ConcatenateRunsText(gifterHeader?.PrimaryText?.Runs)
            ?? gifterHeader?.PrimaryText?.SimpleText;

        var giftCount = 0;

        if (!string.IsNullOrEmpty(headerPrimaryText))
        {
            var giftMatch = GiftedSentCountRegex().Match(headerPrimaryText);

            if (!giftMatch.Success)
            {
                giftMatch = GiftedCountRegex().Match(headerPrimaryText);
            }

            if (giftMatch.Success)
            {
                var countValue = giftMatch.Groups["count"].Value;

                if (string.Equals(countValue, "a", StringComparison.OrdinalIgnoreCase))
                {
                    giftCount = 1;
                }
                else if (int.TryParse(countValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount))
                {
                    giftCount = parsedCount;
                }
            }
            else if (headerPrimaryText.Contains("gift", StringComparison.OrdinalIgnoreCase))
            {
                giftCount = 1;
            }
        }

        var membership = new YouTubeMembershipDetails(
            YouTubeMembershipEventKind.GiftPurchase,
            GiftCount: giftCount,
            GifterDisplayName: effectiveAuthor.DisplayName,
            HeaderText: headerPrimaryText);

        return (effectiveAuthor, membership);
    }

    private static YouTubeMembershipDetails MapGiftRedemption(
        LiveChatSponsorshipsGiftRedemptionAnnouncementRenderer redemption,
        string plainText)
    {
        string? gifterName = null;

        if (redemption.Message?.Runs is [.., var lastRun] && lastRun is MessageText mt)
        {
            gifterName = mt.Text?.Trim();
        }

        var gifterMatch = GiftRedemptionGifterRegex().Match(plainText);

        if (gifterMatch.Success)
        {
            gifterName = gifterMatch.Groups["gifter"].Value.Trim();
        }

        return new(
            YouTubeMembershipEventKind.GiftRedemption,
            GifterDisplayName: gifterName,
            HeaderText: plainText);
    }

    private static (YouTubeLiveChatAuthor Author, string? BadgeLabel) BuildAuthor(MessageRendererBase renderer)
    {
        ParseBadges(
            renderer.AuthorBadges,
            out var isOwner,
            out var isModerator,
            out var isSponsor,
            out var isVerified,
            out var badgeLabel);

        var handle = renderer.AuthorPhoto?.Accessibility?.AccessibilityData?.Label;
        var author = new YouTubeLiveChatAuthor(
            renderer.AuthorExternalChannelId ?? string.Empty,
            renderer.AuthorName?.Text ?? string.Empty,
            isOwner,
            isModerator,
            isSponsor,
            isVerified,
            AvatarUrl: GetLastThumbnailUrl(renderer.AuthorPhoto?.Thumbnails),
            Handle: !string.IsNullOrWhiteSpace(handle) && handle.StartsWith('@') ? handle : null,
            MembershipBadgeLabel: badgeLabel);

        return (author, badgeLabel);
    }

    private static void ParseBadges(
        List<AuthorBadgeContainer>? badges,
        out bool isOwner,
        out bool isModerator,
        out bool isSponsor,
        out bool isVerified,
        out string? membershipBadgeLabel)
    {
        isOwner = false;
        isModerator = false;
        isSponsor = false;
        isVerified = false;
        membershipBadgeLabel = null;

        if (badges is null)
        {
            return;
        }

        foreach (var container in badges)
        {
            var badge = container.LiveChatAuthorBadgeRenderer;

            if (badge is null)
            {
                continue;
            }

            if (badge.CustomThumbnail is not null)
            {
                isSponsor = true;
                membershipBadgeLabel ??= badge.Tooltip ?? badge.Accessibility?.AccessibilityData?.Label;
                continue;
            }

            switch (badge.Icon?.IconType)
            {
                case "OWNER":
                    isOwner = true;
                    break;
                case "MODERATOR":
                    isModerator = true;
                    break;
                case "VERIFIED":
                    isVerified = true;
                    break;
            }
        }
    }

    private static string? ExtractMembershipBadgeLabel(List<AuthorBadgeContainer>? badges)
    {
        if (badges is null)
        {
            return null;
        }

        foreach (var container in badges)
        {
            var badge = container.LiveChatAuthorBadgeRenderer;

            if (badge?.CustomThumbnail is not null)
            {
                return badge.Tooltip ?? badge.Accessibility?.AccessibilityData?.Label;
            }
        }

        return null;
    }

    private static (IReadOnlyList<YouTubeChatRun> Runs, string PlainText) MapMessageRuns(List<MessageRun>? rawRuns)
    {
        if (rawRuns is null || rawRuns.Count == 0)
        {
            return ([], string.Empty);
        }

        var runs = new List<YouTubeChatRun>(rawRuns.Count);
        var parts = new List<string>(rawRuns.Count);

        foreach (var run in rawRuns)
        {
            switch (run)
            {
                case MessageText textRun when textRun.Text is not null:
                    runs.Add(new(YouTubeChatRunKind.Text, textRun.Text));
                    parts.Add(textRun.Text);
                    break;
                case MessageEmoji emojiRun when emojiRun.Emoji is { } emoji:
                {
                    var shortcut = emoji.Shortcuts is [var firstShortcut, ..]
                        ? firstShortcut
                        : emoji.SearchTerms is [var firstSearchTerm, ..]
                            ? firstSearchTerm
                            : null;
                    var displayText = emoji.IsCustomEmoji
                        ? shortcut ?? emoji.EmojiId ?? string.Empty
                        : emoji.EmojiId ?? shortcut ?? string.Empty;
                    var imageUrl = GetLastThumbnailUrl(emoji.Image?.Thumbnails);

                    runs.Add(new(
                        YouTubeChatRunKind.Emoji,
                        displayText,
                        emoji.EmojiId,
                        shortcut,
                        imageUrl,
                        emoji.IsCustomEmoji));
                    parts.Add(displayText);
                    break;
                }
            }
        }

        return (runs, string.Concat(parts));
    }

    private static string? ConcatenateRunsText(List<MessageRun>? runs)
    {
        if (runs is null || runs.Count == 0)
        {
            return null;
        }

        var (_, text) = MapMessageRuns(runs);
        return text;
    }

    private static string? TryExtractTierNameFromRuns(List<MessageRun>? runs)
    {
        if (runs is null)
        {
            return null;
        }

        string? first = null;
        string? middle = null;
        string? last = null;
        var count = 0;

        foreach (var run in runs)
        {
            if (run is not MessageText textRun || string.IsNullOrWhiteSpace(textRun.Text))
            {
                continue;
            }

            count++;

            switch (count)
            {
                case 1:
                    first = textRun.Text;
                    break;
                case 2:
                    middle = textRun.Text.Trim();
                    break;
                case 3:
                    last = textRun.Text.Trim();
                    break;
                default:
                    return null;
            }
        }

        return count == 3
            && first is not null
            && middle is { Length: > 0 }
            && (string.Equals(last, "!", StringComparison.Ordinal) || string.Equals(last, "！", StringComparison.Ordinal))
            && first.EndsWith(' ')
            ? middle
            : null;
    }

    private static string? GetLastThumbnailUrl(List<Thumbnail>? thumbnails)
    {
        return thumbnails is [.., var last] && !string.IsNullOrWhiteSpace(last.Url)
            ? last.Url
            : null;
    }

    private static DateTimeOffset? TryParseTimestampUsec(string? rawUsec)
    {
        if (string.IsNullOrWhiteSpace(rawUsec)
            || !long.TryParse(rawUsec, NumberStyles.Integer, CultureInfo.InvariantCulture, out var usec))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(usec / 1_000);
    }

    private static bool ContainsChatEnded(List<YouTubeLiveChatItem> items)
    {
        foreach (var item in items)
        {
            if (item.Kind == YouTubeLiveChatItemKind.ChatEnded)
            {
                return true;
            }
        }

        return false;
    }

    private static YouTubeLiveChatAuthor EmptyAuthor() =>
        new(string.Empty, string.Empty, false, false, false, false);

    private static string? ExtractVideoId(string html)
    {
        return ExtractGroup(LiveIdRegex().Match(html))
            ?? ExtractGroup(CanonicalBaseUrlLiveIdRegex().Match(html))
            ?? ExtractGroup(ChatTopicLiveIdRegex().Match(html))
            ?? ExtractGroup(VideoDetailsLiveIdRegex().Match(html));
    }

    private static string? ExtractGroup(Match match) =>
        match.Success ? match.Groups["value"].Value : null;

    [GeneratedRegex(
        "<link[^>]*rel=\"canonical\"[^>]*href=\"[^\"]*watch\\?v=(?<value>[A-Za-z0-9_-]{11})[^\"]*\"",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex LiveIdRegex();

    [GeneratedRegex(
        "\"canonicalBaseUrl\":\\s*\"[^\"]*watch\\?v=(?<value>[A-Za-z0-9_-]{11})",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex CanonicalBaseUrlLiveIdRegex();

    [GeneratedRegex(
        "\"topic\":\\s*\"chat~(?<value>[A-Za-z0-9_-]{11})\"",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex ChatTopicLiveIdRegex();

    [GeneratedRegex(
        "\"videoDetails\":\\s*\\{[^\\}]*\"videoId\":\\s*\"(?<value>[A-Za-z0-9_-]{11})\"",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex VideoDetailsLiveIdRegex();

    [GeneratedRegex(
        "\"isReplay\":\\s*true",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex ReplayRegex();

    [GeneratedRegex(
        "\"INNERTUBE_API_KEY\":\\s*\"(?<value>[^\"]*)\"",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex ApiKeyRegex();

    [GeneratedRegex(
        "\"INNERTUBE_CONTEXT_CLIENT_VERSION\":\\s*\"(?<value>[^\"]*)\"",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex ClientVersionRegex();

    [GeneratedRegex(
        "\"continuation\":\\s*\"(?<value>[^\"]*)\"",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex ContinuationRegex();

    [GeneratedRegex(
        @"member for (?<months>\d+) months?",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex MilestoneMonthsRegex();

    [GeneratedRegex(
        @"Gifted (?<count>\d+|a) .*? membership",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex GiftedCountRegex();

    [GeneratedRegex(
        @"^Sent (?<count>\d+) .*? gift memberships?$",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex GiftedSentCountRegex();

    [GeneratedRegex(
        @"Welcome to (?<level>.*?) membership",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex NewMemberLevelRegex();

    [GeneratedRegex(
        @"^Welcome to (?<level>.+)!$",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex NewMemberLevelFromSubtextRegex();

    [GeneratedRegex(
        @"^Upgraded membership to (?<level>.+)!$",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex UpgradedMemberLevelFromSubtextRegex();

    [GeneratedRegex(
        @"^(?<gifter>.*?) gifted you",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex GiftRedemptionGifterRegex();

    [GeneratedRegex(
        @"^sent .+ for (?<jewels>\d+) Jewels?$",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex JewelsGiftTextRegex();
}
