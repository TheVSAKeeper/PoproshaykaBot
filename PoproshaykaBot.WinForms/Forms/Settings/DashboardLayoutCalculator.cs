namespace PoproshaykaBot.WinForms.Forms.Settings;

internal sealed class PlacedTile
{
    public int Row { get; set; }

    public int Column { get; set; }

    public int ColumnSpan { get; set; }

    public int RowSpan { get; set; }

    public int? MaxHeight { get; set; }

    public int? MaxWidth { get; set; }
}

internal static class DashboardLayoutCalculator
{
    public static void ClampPlacement(PlacedTile placed, int columnCount, int rowCount)
    {
        placed.Row = Math.Clamp(placed.Row, 0, rowCount - 1);
        placed.Column = Math.Clamp(placed.Column, 0, columnCount - 1);
        placed.ColumnSpan = Math.Clamp(placed.ColumnSpan, 1, columnCount - placed.Column);
        placed.RowSpan = Math.Clamp(placed.RowSpan, 1, rowCount - placed.Row);
    }

    public static bool[,] BuildOccupancyMap(IEnumerable<PlacedTile> placedTiles, int rowCount, int columnCount)
    {
        var occupied = new bool[rowCount, columnCount];

        foreach (var placed in placedTiles)
        {
            for (var r = placed.Row; r < placed.Row + placed.RowSpan; r++)
            {
                for (var c = placed.Column; c < placed.Column + placed.ColumnSpan; c++)
                {
                    occupied[r, c] = true;
                }
            }
        }

        return occupied;
    }
}
