using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Statistics.Linking;
using PoproshaykaBot.Core.Tests.Twitch.EventSub;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class AccountLinkingServiceTests
{
    private static readonly PlatformUserKey TwitchA = new(StreamingPlatform.Twitch, "tw_a");
    private static readonly PlatformUserKey YouTubeB = new(StreamingPlatform.YouTube, "yt_b");
    private static readonly PlatformUserKey YouTubeC = new(StreamingPlatform.YouTube, "yt_c");
    private static readonly PlatformUserKey AttackerM = new(StreamingPlatform.Twitch, "tw_mallory");

    private UserStatisticsRepository _repo = null!;
    private InMemoryEventBus _eventBus = null!;
    private ManualTimeProvider _time = null!;
    private AccountLinkingService _service = null!;
    private List<AccountLinksChanged> _events = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new();
        _repo = new(NullLogger<UserStatisticsRepository>.Instance, _time);
        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);
        _service = new(_repo, _eventBus, _time);
        _events = [];
        _eventBus.Subscribe<AccountLinksChanged>(e => _events.Add(e));

        var twitchChannel = new ChannelKey(StreamingPlatform.Twitch, "ch_tw");
        var youtubeChannel = new ChannelKey(StreamingPlatform.YouTube, "ch_yt");
        _repo.TrackMessage(twitchChannel, TwitchA, "ViewerA");
        _repo.TrackMessage(youtubeChannel, YouTubeB, "ViewerB");
        _repo.TrackMessage(youtubeChannel, YouTubeC, "ViewerC");
        _repo.TrackMessage(twitchChannel, AttackerM, "Mallory");
    }

    [Test]
    public async Task Viewer_mutual_handshake_creates_ViewerConfirmed_link_with_author_while_hijack_and_expired_codes_fail()
    {
        var step1 = _service.InitiateViewerChallenge(TwitchA, "ViewerA");
        Assert.That(step1.Status, Is.EqualTo(AccountLinkOperationStatus.ChallengeIssued));
        var challengeCode = step1.Code!;

        var hijacked = _service.SubmitViewerClaim(AttackerM, "Mallory", challengeCode);
        Assert.That(hijacked.Status, Is.EqualTo(AccountLinkOperationStatus.ClaimIssued));

        var wrongConfirm = await _service.ConfirmViewerLinkAsync(AttackerM, "Mallory", hijacked.Code!);
        Assert.That(wrongConfirm.Status, Is.EqualTo(AccountLinkOperationStatus.WrongConfirmingAccount));
        Assert.That(_repo.GetLinkGroup(TwitchA), Is.Null);

        var step2 = _service.SubmitViewerClaim(YouTubeB, "ViewerB", challengeCode);
        Assert.That(step2.Status, Is.EqualTo(AccountLinkOperationStatus.ClaimIssued));
        var confirmCode = step2.Code!;

        var step3 = await _service.ConfirmViewerLinkAsync(TwitchA, "ViewerA", confirmCode);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(step3.Status, Is.EqualTo(AccountLinkOperationStatus.Linked));
            Assert.That(_events, Has.Count.EqualTo(1));
            Assert.That(_events[0].GroupId, Is.EqualTo(step3.Group!.GroupId));
            Assert.That(_repo.GetLinkedKeys(TwitchA), Is.EquivalentTo(new[] { TwitchA, YouTubeB }));
            Assert.That(step3.Group.Members.All(static m => m.Origin == AccountLinkOrigin.ViewerConfirmed), Is.True);
            Assert.That(step3.Group.Members.Single(m => m.Key == TwitchA).Author.Kind, Is.EqualTo(AccountLinkAuthorKind.Viewer));
            Assert.That(step3.Group.Members.Single(m => m.Key == TwitchA).Author.ActorUserId, Is.EqualTo(TwitchA.UserId));
        }

        _time.Advance(AccountLinkingService.ChallengeTtl + TimeSpan.FromSeconds(1));
        var expired = await _service.ConfirmViewerLinkAsync(TwitchA, "ViewerA", hijacked.Code!);
        Assert.That(expired.Status, Is.EqualTo(AccountLinkOperationStatus.InvalidOrExpiredCode));
    }

    [Test]
    public async Task Viewer_handshake_rejects_merging_into_another_existing_group()
    {
        var streamer = new AccountLinkAuthor(AccountLinkAuthorKind.Streamer, StreamingPlatform.Twitch, "broadcaster", "Streamer");
        var challenge = _service.InitiateViewerChallenge(TwitchA, "ViewerA");
        var claimBeforeLink = _service.SubmitViewerClaim(YouTubeB, "ViewerB", challenge.Code!);
        Assert.That(claimBeforeLink.Status, Is.EqualTo(AccountLinkOperationStatus.ClaimIssued));

        await _service.LinkManualAsync([YouTubeB, YouTubeC], streamer);

        var claimAfterLink = _service.SubmitViewerClaim(YouTubeB, "ViewerB", challenge.Code!);
        var confirmAfterRace = await _service.ConfirmViewerLinkAsync(TwitchA, "ViewerA", claimBeforeLink.Code!);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(claimAfterLink.Status, Is.EqualTo(AccountLinkOperationStatus.TargetInAnotherGroup));
            Assert.That(confirmAfterRace.Status, Is.EqualTo(AccountLinkOperationStatus.TargetInAnotherGroup));
            Assert.That(_repo.GetLinkedKeys(TwitchA), Is.EquivalentTo(new[] { TwitchA }));
            Assert.That(_repo.GetLinkedKeys(YouTubeB), Is.EquivalentTo(new[] { YouTubeB, YouTubeC }));
        }
    }

    [Test]
    public async Task Self_unlink_from_three_member_group_keeps_remaining_two_linked_and_leaves_counters_intact()
    {
        var streamer = new AccountLinkAuthor(AccountLinkAuthorKind.Streamer, StreamingPlatform.Twitch, "broadcaster", "Streamer");
        var twCh = new ChannelKey(StreamingPlatform.Twitch, "ch_tw");
        var ytCh = new ChannelKey(StreamingPlatform.YouTube, "ch_yt");
        _repo.TrackMessage(twCh, TwitchA, "ViewerA");
        _repo.TrackMessage(ytCh, YouTubeB, "ViewerB");
        _repo.TrackMessage(ytCh, YouTubeC, "ViewerC");

        var manual = await _service.LinkManualAsync([TwitchA, YouTubeB], streamer);
        Assert.That(manual.Status, Is.EqualTo(AccountLinkOperationStatus.Linked));
        var manualAddThird = await _service.LinkManualAsync([TwitchA, YouTubeC], streamer);
        Assert.That(manualAddThird.Status, Is.EqualTo(AccountLinkOperationStatus.Linked));

        var unlinkB = await _service.UnlinkSelfAsync(YouTubeB, "ViewerB");
        var remainingGroup = _repo.GetLinkGroup(TwitchA);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unlinkB.Status, Is.EqualTo(AccountLinkOperationStatus.Unlinked));
            Assert.That(remainingGroup, Is.Not.Null);
            Assert.That(remainingGroup!.Members.Select(static m => m.Key), Is.EquivalentTo(new[] { TwitchA, YouTubeC }));
            Assert.That(remainingGroup.Members.All(static m => m.Origin == AccountLinkOrigin.StreamerManual), Is.True);
            Assert.That(_repo.GetLinkedKeys(YouTubeB), Is.EquivalentTo(new[] { YouTubeB }));
            Assert.That(_repo.FindViewerById(StatisticsSlice.ForChannel(ytCh), YouTubeB)!.MessageCount, Is.EqualTo(2UL));
            Assert.That(_repo.FindViewerById(StatisticsSlice.ForChannel(twCh), TwitchA)!.MessageCount, Is.EqualTo(2UL));
        }

        var unlinkC = await _service.UnlinkSelfAsync(YouTubeC, "ViewerC");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unlinkC.Status, Is.EqualTo(AccountLinkOperationStatus.Unlinked));
            Assert.That(_repo.GetLinkGroup(TwitchA), Is.Null);
            Assert.That(_repo.GetLinkGroup(YouTubeC), Is.Null);
        }
    }

    [Test]
    public async Task Manual_streamer_link_and_unlink_can_also_be_removed_by_viewer_self_unlink()
    {
        var streamer = new AccountLinkAuthor(AccountLinkAuthorKind.Streamer, StreamingPlatform.Twitch, "broadcaster", "Streamer");
        var linked = await _service.LinkManualAsync([TwitchA, YouTubeB], streamer);

        Assert.That(linked.Status, Is.EqualTo(AccountLinkOperationStatus.Linked));
        Assert.That(linked.Group!.Members.All(static m => m.Origin == AccountLinkOrigin.StreamerManual), Is.True);

        var viewerUnlink = await _service.UnlinkSelfAsync(YouTubeB, "ViewerB");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewerUnlink.Status, Is.EqualTo(AccountLinkOperationStatus.Unlinked));
            Assert.That(_repo.GetLinkGroup(TwitchA), Is.Null);
            Assert.That(_repo.GetLinkGroup(YouTubeB), Is.Null);
        }

        await _service.LinkManualAsync([TwitchA, YouTubeB], streamer);
        var manualUnlink = await _service.UnlinkManualAsync(TwitchA, streamer);

        Assert.That(manualUnlink.Status, Is.EqualTo(AccountLinkOperationStatus.Unlinked));
        Assert.That(_repo.GetLinkGroup(TwitchA), Is.Null);
    }

    [Test]
    public async Task Second_confirm_on_same_challenge_cannot_link_second_claimant()
    {
        var challenge = _service.InitiateViewerChallenge(TwitchA, "ViewerA");
        var claimB = _service.SubmitViewerClaim(YouTubeB, "ViewerB", challenge.Code!);
        var claimC = _service.SubmitViewerClaim(YouTubeC, "ViewerC", challenge.Code!);

        var results = await Task.WhenAll(
            _service.ConfirmViewerLinkAsync(TwitchA, "ViewerA", claimB.Code!),
            _service.ConfirmViewerLinkAsync(TwitchA, "ViewerA", claimC.Code!));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(static r => r.Status == AccountLinkOperationStatus.Linked), Is.EqualTo(1));
            Assert.That(results.Count(static r => r.Status == AccountLinkOperationStatus.InvalidOrExpiredCode), Is.EqualTo(1));
            Assert.That(_repo.GetLinkedKeys(TwitchA), Has.Count.EqualTo(2));
        }
    }
}
