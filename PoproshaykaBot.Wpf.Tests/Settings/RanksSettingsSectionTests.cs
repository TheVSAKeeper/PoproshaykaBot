using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.ViewModels.Settings;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public class RanksSettingsSectionTests
{
    private static readonly UserRank[] Saved =
    [
        new("♟", "ПЕШКА", 0, 3),
        new("♔", "КОРОЛЬ", 5000),
        new("♞", "КОНЬ", 300, 3),
    ];

    [Test]
    public void Загрузка_ставит_старшую_ступень_сверху_и_не_считается_правкой()
    {
        var section = new RanksSettingsSectionViewModel();
        var edits = 0;
        section.Edited += (_, _) => edits++;

        section.Load(Saved);

        Assert.Multiple(() =>
        {
            Assert.That(section.Entries.Select(entry => entry.Name), Is.EqualTo(new[] { "КОРОЛЬ", "КОНЬ", "ПЕШКА" }));
            Assert.That(section.BuildRanks(), Is.EqualTo(Saved.OrderByDescending(rank => rank.MinMessages)));
            Assert.That(section.HasErrors, Is.False);
            Assert.That(edits, Is.Zero);
        });
    }

    [Test]
    public void Значок_название_номер_и_порог_правятся_и_доезжают_до_списка()
    {
        var section = new RanksSettingsSectionViewModel();
        section.Load(Saved);
        var edits = 0;
        section.Edited += (_, _) => edits++;

        var king = section.Entries[0];
        king.Emoji = "♚";
        king.Name = "  ИМПЕРАТОР ";
        king.Tier = 1;
        king.ThresholdText = "7 500";

        Assert.Multiple(() =>
        {
            Assert.That(section.BuildRanks()[0], Is.EqualTo(new UserRank("♚", "ИМПЕРАТОР", 7500, 1)));
            Assert.That(edits, Is.EqualTo(4));
        });
    }

    [TestCase("", "Укажите порог")]
    [TestCase("-5", "Порог не может быть отрицательным")]
    [TestCase("−5", "Порог не может быть отрицательным")]
    [TestCase("12.5", "Порог – целое число от 0")]
    [TestCase("сто", "Порог – целое число от 0")]
    [TestCase("99999999999999999999", "Порог – целое число от 0")]
    public void Недопустимый_порог_называет_причину(string text, string expected)
    {
        var section = new RanksSettingsSectionViewModel();
        section.Load(Saved);

        section.Entries[1].ThresholdText = text;

        Assert.Multiple(() =>
        {
            Assert.That(section.Entries[1].ThresholdError, Is.EqualTo(expected));
            Assert.That(section.HasErrors, Is.True);
        });
    }

    [Test]
    public void Повтор_порога_называет_ступень_у_которой_он_уже_есть()
    {
        var section = new RanksSettingsSectionViewModel();
        section.Load(Saved);

        section.Entries[2].ThresholdText = "300";

        Assert.Multiple(() =>
        {
            Assert.That(section.Entries[2].ThresholdError, Is.EqualTo("Такой порог уже у «КОНЬ III»"));
            Assert.That(section.Entries[1].HasThresholdError, Is.False);
            Assert.That(section.HasErrors, Is.True);
        });

        section.Entries[2].ThresholdText = "0";

        Assert.That(section.HasErrors, Is.False, "Исправленный порог снимает ошибку");
    }

    [TestCase("", "ПЕШКА", "Укажите значок", null)]
    [TestCase("♟", " ", null, "Укажите название")]
    public void Пустой_значок_или_название_называют_причину(string emoji, string name, string? emojiError, string? nameError)
    {
        var section = new RanksSettingsSectionViewModel();
        section.Load(Saved);

        section.Entries[2].Emoji = emoji;
        section.Entries[2].Name = name;

        Assert.Multiple(() =>
        {
            Assert.That(section.Entries[2].EmojiError, Is.EqualTo(emojiError));
            Assert.That(section.Entries[2].NameError, Is.EqualTo(nameError));
            Assert.That(section.HasErrors, Is.True);
        });
    }

    [Test]
    public void Новая_ступень_встаёт_над_старшей_без_ошибок()
    {
        var section = new RanksSettingsSectionViewModel();
        section.Load(Saved);
        var edits = 0;
        section.Edited += (_, _) => edits++;

        section.AddCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(section.Entries, Has.Count.EqualTo(4));
            Assert.That(section.Entries[0].ThresholdText, Is.EqualTo("6000"));
            Assert.That(section.HasErrors, Is.False);
            Assert.That(edits, Is.EqualTo(1));
        });
    }

    [Test]
    public void Последнюю_ступень_удалить_нельзя()
    {
        var section = new RanksSettingsSectionViewModel();
        section.Load(Saved);

        section.RemoveCommand.Execute(section.Entries[0]);
        section.RemoveCommand.Execute(section.Entries[0]);

        Assert.Multiple(() =>
        {
            Assert.That(section.Entries.Select(entry => entry.Name), Is.EqualTo(new[] { "ПЕШКА" }));
            Assert.That(section.RemoveCommand.CanExecute(section.Entries[0]), Is.False);
        });
    }

    [Test]
    public void Сброс_возвращает_умолчания_и_считается_правкой()
    {
        var section = new RanksSettingsSectionViewModel();
        section.Load(Saved);
        var edits = 0;
        section.Edited += (_, _) => edits++;

        section.ResetToDefaultsCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(section.BuildRanks(), Is.EqualTo(new RanksSettings().Ranks));
            Assert.That(edits, Is.EqualTo(1));
        });
    }
}
