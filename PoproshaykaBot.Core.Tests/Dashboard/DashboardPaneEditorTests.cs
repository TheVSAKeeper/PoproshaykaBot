using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Ui;

namespace PoproshaykaBot.Core.Tests.Dashboard;

[TestFixture]
public sealed class DashboardPaneEditorTests
{
    [TestCase(PaneSide.Left, "new", "stream-info")]
    [TestCase(PaneSide.Right, "stream-info", "new")]
    [TestCase(PaneSide.Top, "new", "stream-info")]
    [TestCase(PaneSide.Bottom, "stream-info", "new")]
    public void Split_SingleLeaf_WrapsItIntoTwoEqualHalves(PaneSide side, string first, string second)
    {
        var split = Split(new TilePane("stream-info"), "stream-info", side, "new") as SplitPane;

        Assert.That(split, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(split!.Orientation, Is.EqualTo(side is PaneSide.Left or PaneSide.Right ? SplitOrientation.Columns : SplitOrientation.Rows));
            Assert.That(Leaves(split), Is.EqualTo(new[] { first, second }));
            Assert.That(Weights(split), Is.EqualTo(new double?[] { 0.5, 0.5 }));
        }
    }

    [Test]
    public void Split_AlongTheParentOrientation_InsertsASiblingInsteadOfNesting()
    {
        var root = Columns(Leaf("stream-info", 0.6), Leaf("polls-control", 0.4));

        var split = (SplitPane)Split(root, "stream-info", PaneSide.Right, "obs-info");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Leaves(split), Is.EqualTo(new[] { "stream-info", "obs-info", "polls-control" }),
                "Разрез вдоль оси узла даёт соседа, а не вложенный узел: иначе разделитель двигает не ту границу.");
            Assert.That(Weights(split), Is.EqualTo(new double?[] { 0.3, 0.3, 0.4 }).Within(1e-9));
        }
    }

    [Test]
    public void Split_AcrossTheParentOrientation_NestsAndKeepsTheSlotWeight()
    {
        var root = Columns(Leaf("stream-info", 0.6), Leaf("polls-control", 0.4));

        var split = (SplitPane)Split(root, "stream-info", PaneSide.Bottom, "obs-info");

        var nested = split.Children[0].Pane as SplitPane;

        Assert.That(nested, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(split.Children[0].Weight, Is.EqualTo(0.6).Within(1e-9), "Ширина колонки от разреза по строкам не меняется.");
            Assert.That(nested!.Orientation, Is.EqualTo(SplitOrientation.Rows));
            Assert.That(Leaves(nested), Is.EqualTo(new[] { "stream-info", "obs-info" }));
        }
    }

    [Test]
    public void Split_LeafWithAutoWeight_KeepsBothHalvesAuto()
    {
        var root = Columns(Leaf("stream-info", null), Leaf("polls-control", 0.4));

        var split = (SplitPane)Split(root, "stream-info", PaneSide.Left, "obs-info");

        Assert.That(Weights(split), Is.EqualTo(new double?[] { null, null, 0.4 }),
            "«По контенту» – это и свёрнутая плитка тоже, разрез не имеет права подменять её долей.");
    }

    [TestCase("stream-info", PaneSide.Left, "polls-control", TestName = "Split_TypeIdAlreadyInTheTree")]
    [TestCase("nothing", PaneSide.Left, "obs-info", TestName = "Split_UnknownTarget")]
    [TestCase("stream-info", PaneSide.None, "obs-info", TestName = "Split_WithoutASide")]
    [TestCase("stream-info", PaneSide.Left, "", TestName = "Split_WithoutATypeId")]
    public void Split_ImpossibleRequest_LeavesTheTreeAlone(string target, PaneSide side, string typeId)
    {
        var root = Columns(Leaf("stream-info", 0.5), Leaf("polls-control", 0.5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(DashboardPaneEditor.TrySplit(root, target, side, typeId, out var result), Is.False);
            Assert.That(result, Is.SameAs(root));
        }
    }

    [Test]
    public void Swap_TwoLeaves_ExchangesThemAndLeavesTheWeightsWithThePositions()
    {
        var root = Columns(Leaf("stream-info", 0.7), Rows(Leaf("polls-control", 0.5), Leaf("obs-info", 0.5), 0.3));

        Assert.That(DashboardPaneEditor.TrySwap(root, "stream-info", "obs-info", out var swapped), Is.True);

        var split = (SplitPane)swapped;
        var nested = (SplitPane)split.Children[1].Pane;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Leaves(split), Is.EqualTo(new[] { "obs-info" }));
            Assert.That(Leaves(nested), Is.EqualTo(new[] { "polls-control", "stream-info" }));
            Assert.That(split.Children[0].Weight, Is.EqualTo(0.7).Within(1e-9), "Обмен местами не меняет пропорции – меняются жильцы, а не комнаты.");
        }
    }

    [TestCase("stream-info", "nothing")]
    [TestCase("stream-info", "stream-info")]
    [TestCase("", "polls-control")]
    public void Swap_ImpossibleRequest_LeavesTheTreeAlone(string first, string second)
    {
        var root = Columns(Leaf("stream-info", 0.5), Leaf("polls-control", 0.5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(DashboardPaneEditor.TrySwap(root, first, second, out var result), Is.False);
            Assert.That(result, Is.SameAs(root));
        }
    }

    [Test]
    public void Remove_OneOfThreeSiblings_SpreadsItsShareOverTheRest()
    {
        var root = Columns(Leaf("stream-info", 0.2), Leaf("polls-control", 0.3), Leaf("obs-info", 0.5));

        var split = (SplitPane)Remove(root, "obs-info")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Leaves(split), Is.EqualTo(new[] { "stream-info", "polls-control" }));
            Assert.That(Weights(split), Is.EqualTo(new double?[] { 0.4, 0.6 }).Within(1e-9));
        }
    }

    [Test]
    public void Remove_LastNeighbourInANode_CollapsesTheNodeIntoItsParent()
    {
        var root = Columns(Leaf("stream-info", 0.6), Rows(Leaf("polls-control", 0.5), Leaf("obs-info", 0.5), 0.4));

        var split = (SplitPane)Remove(root, "obs-info")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Leaves(split), Is.EqualTo(new[] { "stream-info", "polls-control" }));
            Assert.That(Weights(split), Is.EqualTo(new double?[] { 0.6, 0.4 }).Within(1e-9), "Схлопнутый узел отдаёт свою долю уцелевшему ребёнку.");
        }
    }

    [Test]
    public void Remove_NodeThatCollapsesIntoASplitOfTheSameOrientation_IsFlattened()
    {
        var inner = Columns(Leaf("polls-control", 0.5), Leaf("obs-info", 0.5));
        var root = Columns(Leaf("stream-info", 0.5), Rows(new(inner, 0.5), Leaf("logs", 0.5), 0.5));

        var split = (SplitPane)Remove(root, "logs")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Leaves(split), Is.EqualTo(new[] { "stream-info", "polls-control", "obs-info" }),
                "Вложенный узел той же ориентации – лишний уровень: разделитель на нём двигал бы границу, которой нет.");
            Assert.That(Weights(split), Is.EqualTo(new double?[] { 0.5, 0.25, 0.25 }).Within(1e-9));
        }
    }

    [Test]
    public void Remove_FlattenedNodeWithWeightsNotSummingToOne_KeepsTheProportions()
    {
        var inner = Columns(Leaf("polls-control", 2), Leaf("obs-info", 6));
        var root = Columns(Leaf("stream-info", 0.2), new(inner, 0.6), Leaf("logs", 0.2));

        var split = (SplitPane)Remove(root, "logs")!;

        Assert.That(Weights(split), Is.EqualTo(new double?[] { 0.25, 0.1875, 0.5625 }).Within(1e-9),
            "Доли внутри узла относительны его сумме, поэтому разворачивание обязано сперва привести их к долям, а потом умножать.");
    }

    [Test]
    public void Split_AtTheMaximumDepth_Refuses()
    {
        var root = Nested(32);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(DashboardPaneEditor.IsWellFormed(root), Is.True);
            Assert.That(DashboardPaneEditor.TrySplit(root, "leaf-0", PaneSide.Left, "obs-info", out var result), Is.False,
                "Разрез, уводящий дерево за потолок глубины, не должен отдаваться как успешный: спроецировать его уже нельзя.");
            Assert.That(result, Is.SameAs(root));
        }
    }

    [Test]
    public void Normalize_WeightsThatOverflowTheirSum_StayFiniteOrBecomeAuto()
    {
        var root = Columns(Leaf("stream-info", double.MaxValue), Leaf("polls-control", double.MaxValue / 2));

        var weights = Weights((SplitPane)DashboardPaneEditor.Normalize(root)!);

        Assert.That(weights, Is.All.Matches<double?>(weight => weight is null || double.IsFinite(weight.Value)),
            "Бесконечность в весе валит сериализацию файла, а ноль – рендер: и то и другое обязано выродиться в «по контенту».");
    }

    [Test]
    public void Normalize_WeightsSoSmallTheirScaleOverflows_StayFiniteOrBecomeAuto()
    {
        var root = Columns(Leaf("stream-info", double.Epsilon), Leaf("polls-control", double.Epsilon));

        var weights = Weights((SplitPane)DashboardPaneEditor.Normalize(root)!);

        Assert.That(weights, Is.All.Matches<double?>(weight => weight is null || double.IsFinite(weight.Value)));
    }

    [Test]
    public void Remove_TheOnlyLeaf_LeavesNoTree()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(DashboardPaneEditor.TryRemove(new TilePane("stream-info"), "stream-info", out var result), Is.True);
            Assert.That(result, Is.Null);
        }
    }

    [TestCase("nothing")]
    [TestCase("")]
    public void Remove_ImpossibleRequest_LeavesTheTreeAlone(string typeId)
    {
        var root = Columns(Leaf("stream-info", 0.5), Leaf("polls-control", 0.5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(DashboardPaneEditor.TryRemove(root, typeId, out var result), Is.False);
            Assert.That(result, Is.SameAs(root));
        }
    }

    [Test]
    public void Normalize_WeightsThatDoNotSumToOne_AreScaledToTheirShares()
    {
        var root = Columns(Leaf("stream-info", 2), Leaf("polls-control", 6));

        Assert.That(Weights((SplitPane)DashboardPaneEditor.Normalize(root)!), Is.EqualTo(new double?[] { 0.25, 0.75 }).Within(1e-9));
    }

    [Test]
    public void Normalize_UnusableWeight_BecomesAuto()
    {
        var root = Columns(Leaf("stream-info", double.NaN), Leaf("polls-control", -1), Leaf("obs-info", 0.5));

        Assert.That(Weights((SplitPane)DashboardPaneEditor.Normalize(root)!), Is.EqualTo(new double?[] { null, null, 0.5 }),
            "Мусорный вес – это «по контенту», а не деление на ноль в рендере.");
    }

    [Test]
    public void Normalize_ExplicitWeightsThatLeaveNothingToAuto_AreCappedForIt()
    {
        var root = Columns(Leaf("stream-info", 0.8), Leaf("polls-control", 0.8), Leaf("obs-info", null));

        Assert.That(Weights((SplitPane)DashboardPaneEditor.Normalize(root)!), Is.EqualTo(new double?[] { 1.0 / 3, 1.0 / 3, null }).Within(1e-9),
            "Свёрнутой плитке обязана остаться доля, иначе она исчезнет с панели.");
    }

    [Test]
    public void Normalize_NodeOfAutoOnly_IsLeftAlone()
    {
        var root = Columns(Leaf("stream-info", null), Leaf("polls-control", null));

        Assert.That(Weights((SplitPane)DashboardPaneEditor.Normalize(root)!), Is.EqualTo(new double?[] { null, null }));
    }

    [TestCaseSource(nameof(MalformedTrees))]
    public void Operations_OnAMalformedTree_Refuse(DashboardPane root)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(DashboardPaneEditor.IsWellFormed(root), Is.False);
            Assert.That(DashboardPaneEditor.TrySplit(root, "stream-info", PaneSide.Left, "obs-info", out _), Is.False);
            Assert.That(DashboardPaneEditor.TrySwap(root, "stream-info", "polls-control", out _), Is.False);
            Assert.That(DashboardPaneEditor.TryRemove(root, "stream-info", out _), Is.False);
            Assert.That(DashboardPaneEditor.Normalize(root), Is.Null);
        }
    }

    public static IEnumerable<TestCaseData> MalformedTrees()
    {
        yield return new TestCaseData(Columns(Leaf("stream-info", 0.5), Leaf("stream-info", 0.5)))
            .SetName("Malformed_DuplicateTypeId");

        yield return new TestCaseData(new SplitPane(SplitOrientation.Columns, [Leaf("stream-info", 1)]))
            .SetName("Malformed_SplitWithOneChild");

        yield return new TestCaseData(new SplitPane(SplitOrientation.None, [Leaf("stream-info", 0.5), Leaf("polls-control", 0.5)]))
            .SetName("Malformed_SplitWithoutOrientation");

        yield return new TestCaseData(Columns(Leaf(string.Empty, 0.5), Leaf("polls-control", 0.5)))
            .SetName("Malformed_LeafWithoutTypeId");

        yield return new TestCaseData(Nested(64)).SetName("Malformed_TooDeep");
    }

    private static DashboardPane Split(DashboardPane root, string target, PaneSide side, string typeId)
    {
        Assert.That(DashboardPaneEditor.TrySplit(root, target, side, typeId, out var result), Is.True);

        return result;
    }

    private static DashboardPane? Remove(DashboardPane root, string typeId)
    {
        Assert.That(DashboardPaneEditor.TryRemove(root, typeId, out var result), Is.True);

        return result;
    }

    private static DashboardPane Nested(int depth)
    {
        DashboardPane pane = new TilePane("leaf-0");

        for (var index = 1; index <= depth; index++)
        {
            var orientation = index % 2 == 1 ? SplitOrientation.Rows : SplitOrientation.Columns;

            pane = new SplitPane(orientation, [new(pane, 0.5), Leaf($"leaf-{index}", 0.5)]);
        }

        return pane;
    }

    private static SplitPane Columns(PaneSlot first, PaneSlot second, params PaneSlot[] rest)
    {
        return new(SplitOrientation.Columns, [first, second, .. rest]);
    }

    private static SplitPane Rows(PaneSlot first, PaneSlot second)
    {
        return new(SplitOrientation.Rows, [first, second]);
    }

    private static PaneSlot Rows(PaneSlot first, PaneSlot second, double? weight)
    {
        return new(Rows(first, second), weight);
    }

    private static PaneSlot Leaf(string typeId, double? weight)
    {
        return new(new TilePane(typeId), weight);
    }

    private static string[] Leaves(SplitPane split)
    {
        return split.Children.Select(slot => slot.Pane).OfType<TilePane>().Select(tile => tile.TypeId).ToArray();
    }

    private static double?[] Weights(SplitPane split)
    {
        return split.Children.Select(slot => slot.Weight).ToArray();
    }
}
