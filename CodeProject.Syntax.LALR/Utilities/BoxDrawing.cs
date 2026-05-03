using System;
using System.Text;

namespace CodeProject.Syntax.LALR.Utilities;

internal static class BoxDrawing
{
    internal enum Edge
    {
        Top,
        Middle,
        Bottom,
    }

    public static StringBuilder HzEdge(this StringBuilder @this, int nColumns, Edge edge = Edge.Middle)
    {
        for (var column = 0; column <= nColumns; column++)
        {
            HzEdge(@this, column, nColumns, edge);
        }
        return @this.AppendLine();
    }

    private static void HzEdge(StringBuilder builder, int column, int nColumns, Edge edge)
    {
        builder.Append(column == 0 ? LeftCorner(edge) : Intersect(edge))
               .Append("──────");

        if (column == nColumns)
        {
            builder.Append(RightCorner(edge));
        }
    }

    private static char LeftCorner(Edge edge) => edge switch
    {
        Edge.Top => '┌',
        Edge.Middle => '├',
        Edge.Bottom => '└',
        _ => throw new ArgumentException("Invalid edge (left)", nameof(edge)),
    };

    private static char Intersect(Edge edge) => edge switch
    {
        Edge.Top => '┬',
        Edge.Middle => '┼',
        Edge.Bottom => '┴',
        _ => throw new ArgumentException("Invalid edge (intersect)", nameof(edge)),
    };

    private static char RightCorner(Edge edge) => edge switch
    {
        Edge.Top => '┐',
        Edge.Middle => '┤',
        Edge.Bottom => '┘',
        _ => throw new ArgumentException("Invalid edge (right)", nameof(edge)),
    };
}
