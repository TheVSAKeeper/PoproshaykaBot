using System.Collections.Immutable;

namespace PoproshaykaBot.Core.Settings.Obs;

public static class MessageAnimationType
{
    public const string None = "no-animation";

    public const string SlideInRight = "slide-in-right";
    public const string SlideInLeft = "slide-in-left";
    public const string FadeInUp = "fade-in-up";
    public const string BounceIn = "bounce-in";
    public const string PopIn = "pop-in";
    public const string RubberBand = "rubber-band";
    public const string Tada = "tada";
    public const string ZoomBlurIn = "zoom-blur-in";
    public const string NeonPulseIn = "neon-pulse-in";
    public const string Materialize = "materialize";
    public const string GlitchIn = "glitch-in";
    public const string PowerUp = "power-up";
    public const string SlamIn = "slam-in";
    public const string NotificationBell = "notification-bell";
    public const string Hologram = "hologram";
    public const string FlipInX = "flip-in-x";
    public const string RollIn = "roll-in";
    public const string CoinFlip = "coin-flip";
    public const string SwoopIn = "swoop-in";

    public const string FadeOut = "fade-out";
    public const string SlideOutLeft = "slide-out-left";
    public const string SlideOutRight = "slide-out-right";
    public const string ScaleDown = "scale-down";
    public const string ShrinkUp = "shrink-up";
    public const string Dissolve = "dissolve";
    public const string SlideOutUp = "slide-out-up";
    public const string SlideOutDown = "slide-out-down";
    public const string RotateOut = "rotate-out";
    public const string PixelateOut = "pixelate-out";

    public static readonly (string Value, string DisplayName)[] EntryAnimations =
    [
        (None, "Без анимации"),
        (SlideInRight, "Скольжение справа"),
        (SlideInLeft, "Скольжение слева"),
        (FadeInUp, "Затухание сверху"),
        (BounceIn, "Прыжок"),
        (PopIn, "Поп-ап с упругостью"),
        (RubberBand, "Резиновая лента"),
        (Tada, "Та-да!"),
        (ZoomBlurIn, "Кинонаезд с размытием"),
        (NeonPulseIn, "Неоновая вспышка"),
        (Materialize, "Материализация"),
        (GlitchIn, "Глитч"),
        (PowerUp, "Усиление снизу"),
        (SlamIn, "Удар сверху"),
        (NotificationBell, "Звон уведомления"),
        (Hologram, "Голограмма"),
        (FlipInX, "Переворот по горизонтали"),
        (RollIn, "Вкатывание"),
        (CoinFlip, "Подброс монеты"),
        (SwoopIn, "Налёт по диагонали"),
    ];

    public static readonly (string Value, string DisplayName)[] ExitAnimations =
    [
        (None, "Без анимации"),
        (FadeOut, "Исчезновение"),
        (SlideOutLeft, "Выскользнуть влево"),
        (SlideOutRight, "Выскользнуть вправо"),
        (ScaleDown, "Уменьшение"),
        (ShrinkUp, "Свернуться вверх"),
        (Dissolve, "Растворение в пыль"),
        (SlideOutUp, "Выскользнуть вверх"),
        (SlideOutDown, "Выскользнуть вниз"),
        (RotateOut, "Поворот с уходом"),
        (PixelateOut, "Пикселизация"),
    ];

    public static readonly ImmutableArray<string> EntryValues =
        [..EntryAnimations.Select(animation => animation.Value)];

    public static readonly ImmutableArray<string> ExitValues =
        [..ExitAnimations.Select(animation => animation.Value)];
}
