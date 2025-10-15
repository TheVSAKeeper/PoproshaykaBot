using PoproshaykaBot.Core.Domain.Models.Chat;
using PoproshaykaBot.Core.Domain.Models.Ui;

namespace PoproshaykaBot.Core.Domain.Models.Settings;

public class ObsChatSettings
{
    public AppColor BackgroundColor { get; set; } = AppColor.FromArgb(179, 0, 0, 0);
    public AppColor TextColor { get; set; } = AppColor.FromArgb(255, 255, 255);
    public AppColor UsernameColor { get; set; } = AppColor.FromArgb(145, 70, 255);
    public AppColor SystemMessageColor { get; set; } = AppColor.FromArgb(255, 204, 0);
    public AppColor TimestampColor { get; set; } = AppColor.FromArgb(153, 153, 153);

    public string FontFamily { get; set; } = "Arial, sans-serif";
    public int FontSize { get; set; } = 14;
    public bool FontBold { get; set; } = false;

    public int Padding { get; set; } = 5;
    public int Margin { get; set; } = 5;
    public int BorderRadius { get; set; } = 5;

    public int AnimationDuration { get; set; } = 300;
    public bool EnableAnimations { get; set; } = true;

    public int MaxMessages { get; set; } = 50;
    public bool ShowTimestamp { get; set; } = true;

    public int EmoteSizePixels { get; set; } = 28;
    public int BadgeSizePixels { get; set; } = 18;

    public bool ShowUserTypeBorders { get; set; } = true;
    public bool HighlightFirstTimeUsers { get; set; } = true;
    public bool HighlightMentions { get; set; } = true;
    public bool EnableMessageShadows { get; set; } = true;
    public bool EnableSpecialEffects { get; set; } = true;

    public bool EnableSmoothScroll { get; set; } = true;
    public int ScrollAnimationDuration { get; set; } = 300;
    public bool AutoScrollEnabled { get; set; } = true;
    public int ScrollToBottomThreshold { get; set; } = 100;

    public string UserMessageAnimation { get; set; } = MessageAnimationType.SlideInRight;
    public string BotMessageAnimation { get; set; } = MessageAnimationType.FadeInUp;
    public string SystemMessageAnimation { get; set; } = MessageAnimationType.FadeInUp;
    public string BroadcasterMessageAnimation { get; set; } = MessageAnimationType.SlideInLeft;
    public string FirstTimeUserMessageAnimation { get; set; } = MessageAnimationType.BounceIn;

    public bool EnableMessageFadeOut { get; set; } = true;
    public int MessageLifetimeSeconds { get; set; } = 30;
    public string FadeOutAnimationType { get; set; } = MessageAnimationType.FadeOut;
    public int FadeOutAnimationDurationMs { get; set; } = 1000;
}