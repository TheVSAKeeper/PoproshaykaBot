namespace PoproshaykaBot.Core.Domain.Models.Chat;

public static class MessageAnimationType
{
    public const string None = "no-animation";
    public const string SlideInRight = "slide-in-right";
    public const string SlideInLeft = "slide-in-left";
    public const string FadeInUp = "fade-in-up";
    public const string BounceIn = "bounce-in";

    public const string FadeOut = "fade-out";
    public const string SlideOutLeft = "slide-out-left";
    public const string SlideOutRight = "slide-out-right";
    public const string ScaleDown = "scale-down";
    public const string ShrinkUp = "shrink-up";

    public static readonly string[] DisplayNames =
    [
        "Без анимации",
        "Скольжение справа",
        "Скольжение слева",
        "Затухание сверху",
        "Прыжок",
        "Исчезновение",
        "Выскользнуть влево",
        "Выскользнуть вправо",
        "Уменьшение",
        "Свернуться вверх",
    ];
}
