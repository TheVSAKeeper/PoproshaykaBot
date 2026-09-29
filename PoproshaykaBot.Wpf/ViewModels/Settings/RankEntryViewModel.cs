using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Users;
using System.Globalization;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class RankEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _emoji;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private int _tier;

    [ObservableProperty]
    private string _thresholdText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmojiError))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _emojiError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _nameError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThresholdError))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _thresholdError;

    public RankEntryViewModel(UserRank rank)
    {
        _emoji = rank.Emoji;
        _name = rank.Name;
        _tier = rank.Tier;
        _thresholdText = rank.MinMessages.ToString(CultureInfo.InvariantCulture);
        TierOptions = RankTierOption.For(rank.Tier);
    }

    public IReadOnlyList<RankTierOption> TierOptions { get; }

    public string DisplayName => new UserRank(Emoji, Name.Trim(), 0, Tier).DisplayName;

    public bool HasEmojiError => EmojiError is not null;

    public bool HasNameError => NameError is not null;

    public bool HasThresholdError => ThresholdError is not null;

    public bool HasError => HasEmojiError || HasNameError || HasThresholdError;

    public static string? ParseThreshold(string? text, out ulong value)
    {
        value = 0;

        var compact = string.Concat((text ?? string.Empty).Where(symbol => !char.IsWhiteSpace(symbol)));

        if (compact.Length == 0)
        {
            return "Укажите порог";
        }

        if (compact[0] is '-' or '−' && compact.Length > 1 && compact[1..].All(char.IsAsciiDigit))
        {
            return "Порог не может быть отрицательным";
        }

        if (!long.TryParse(compact, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return "Порог – целое число от 0";
        }

        value = (ulong)parsed;
        return null;
    }

    public UserRank ToRank()
    {
        ParseThreshold(ThresholdText, out var threshold);
        return new(Emoji.Trim(), Name.Trim(), threshold, Tier);
    }
}
