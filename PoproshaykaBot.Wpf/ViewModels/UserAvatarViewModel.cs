using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Globalization;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class UserAvatarViewModel : ObservableObject
{
    public const string UnknownInitial = "?";

    public static readonly UserAvatarViewModel None = new(string.Empty, string.Empty);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private ImageSource? _image;

    public UserAvatarViewModel(string userId, string name)
    {
        UserId = userId;
        Name = name;
        Initial = InitialOf(name);
        PaletteIndex = StreamSegmentPalette.IndexOf(name);
    }

    public string UserId { get; }

    public string Name { get; }

    public string Initial { get; }

    public int PaletteIndex { get; }

    public bool HasImage => Image is not null;

    internal bool IsOutdated { get; set; }

    private static string InitialOf(string name)
    {
        var elements = StringInfo.GetTextElementEnumerator(name);

        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();

            if (char.IsLetterOrDigit(element, 0))
            {
                return element.ToUpper(UiCulture.Russian);
            }
        }

        return UnknownInitial;
    }
}
