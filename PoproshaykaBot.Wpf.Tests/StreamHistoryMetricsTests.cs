using KeepShell.Testing;
using PoproshaykaBot.Wpf.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class StreamHistoryMetricsTests
{
    private const double GameColumnMinWidth = 90;
    private const double SegmentGameColumnMinWidth = 110;
    private const double SegmentTitleColumnMinWidth = 80;
    private const double SegmentCaptionWidth = 56;
    private const double SegmentPadding = 12;
    private const double SplitterWidth = 12;
    private const double TableShare = 3d / 5;
    private const double NumericHeaderChrome = 8 + 10 + 14;
    private const double CellMargin = 10;

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Подпись_сегмента_показывает_начало_названия_на_пороге()
    {
        var root = Themed();
        var caption = MeasureText(root, "Mine…", (double)root.FindResource("Font.Size.Caption"), null);

        Assert.That(SegmentCaptionWidth - SegmentPadding, Is.GreaterThanOrEqualTo(caption),
            $"На пороге {SegmentCaptionWidth} DIP подписи остаётся {SegmentCaptionWidth - SegmentPadding:F1} DIP, а четырём буквам с многоточием нужно {caption:F1} – подпись выродится в одно многоточие");
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Таблица_влезает_в_свою_колонку_на_пороге_раскладки()
    {
        var root = Themed();

        var required = Column(root, "Начало", "06.09.2026 08:34", mono: true)
                       + Column(root, "Эфир", "2 ч 54 мин")
                       + 90
                       + GameColumnMinWidth
                       + Column(root, "Сообщ.", "175")
                       + Column(root, "Чаттеры", "9")
                       + Column(root, "Пик", "13")
                       + Column(root, "Средн.", "12");

        var available = (StreamHistoryPageView.SideBySideWidth - SplitterWidth) * TableShare;

        Assert.Multiple(() =>
        {
            Assert.That(available, Is.GreaterThanOrEqualTo(required),
                $"На пороге {StreamHistoryPageView.SideBySideWidth} DIP таблица получает {available:F1} DIP, а её колонкам на минимуме нужно {required:F1} – рядом с карточкой она уйдёт в горизонтальную прокрутку");
            Assert.That(StreamHistoryPageView.TableMinWidth, Is.GreaterThanOrEqualTo(required),
                $"Пол колонки таблицы ({StreamHistoryPageView.TableMinWidth} DIP) ниже нужных ей {required:F1} – разделителем её можно будет загнать в горизонтальную прокрутку");
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Карточка_стрима_разъезжается_только_когда_обе_панели_влезают()
    {
        var root = Themed();

        var full = SegmentGameColumnMinWidth
                   + SegmentTitleColumnMinWidth
                   + Column(root, "Эфир", "1 ч 25 мин")
                   + Column(root, "Сообщ.", "175")
                   + Column(root, "Пик", "13")
                   + Column(root, "Средн.", "12");

        var compact = SegmentGameColumnMinWidth + SegmentTitleColumnMinWidth + Column(root, "Эфир", "1 ч 25 мин");

        Assert.Multiple(() =>
        {
            Assert.That(StreamHistoryPageView.DetailSplitWidth,
                Is.GreaterThanOrEqualTo(full + SplitterWidth + StreamHistoryPageView.ChattersMinWidth),
                $"Порог {StreamHistoryPageView.DetailSplitWidth} DIP ниже нужных {full + SplitterWidth + StreamHistoryPageView.ChattersMinWidth:F1} – таблица сегментов уйдёт в горизонтальную прокрутку рядом с чаттерами");
            Assert.That(StreamHistoryPageView.DetailMinWidth, Is.GreaterThanOrEqualTo(compact),
                $"В стопке карточки таблице сегментов остаётся её пол {StreamHistoryPageView.DetailMinWidth} DIP, а трём оставшимся колонкам нужно {compact:F1}");
        });
    }

    [Test]
    public void Пороги_идут_с_гистерезисом()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StreamHistoryPageView.StackedWidth, Is.LessThan(StreamHistoryPageView.SideBySideWidth),
                "без гистерезиса раскладка страницы дрожит на одном пикселе");
            Assert.That(StreamHistoryPageView.DetailStackWidth, Is.LessThan(StreamHistoryPageView.DetailSplitWidth),
                "без гистерезиса раскладка карточки дрожит на одном пикселе");
            Assert.That(StreamHistoryPageView.TableMinWidth + StreamHistoryPageView.DetailMinWidth,
                Is.LessThan(StreamHistoryPageView.SideBySideWidth),
                "полы колонок не должны превышать порог, иначе сетка пересилит их сразу после переключения");
        });
    }

    private static Grid Themed()
    {
        PackScheme.Ensure();

        var root = new Grid();

        foreach (var source in new[]
                 {
                     "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
                     "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
                     "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
                 })
        {
            root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source) });
        }

        return root;
    }

    private static double Column(Grid root, string header, string cell, bool mono = false)
    {
        var size = (double)root.FindResource("Font.Size.Body");
        var family = mono ? (FontFamily)root.FindResource("Font.Mono") : null;

        return Math.Max(
            MeasureText(root, header, size, null) + NumericHeaderChrome,
            MeasureText(root, cell, size, family) + CellMargin);
    }

    private static double MeasureText(Grid root, string text, double fontSize, FontFamily? family)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            TextWrapping = TextWrapping.NoWrap,
        };

        if (family is not null)
        {
            block.FontFamily = family;
        }

        root.Children.Clear();
        root.Children.Add(block);
        root.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        root.UpdateLayout();

        return block.DesiredSize.Width;
    }
}
