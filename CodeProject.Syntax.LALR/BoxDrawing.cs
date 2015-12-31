using System;
using System.Text;

namespace CodeProject.Syntax.LALR
{
    static class BoxDrawing
    {
        internal enum Edge
        {
            Top,
            Middle,
            Bottom
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

        private static char LeftCorner(Edge edge)
        {
            switch (edge)
            {
                case Edge.Top:
                    return '┌';

                case Edge.Middle:
                    return '├';

                case Edge.Bottom:
                    return '└';

                default:
                    throw new ArgumentException("Invalid edge (left)", "edge");
            }
        }

        private static char Intersect(Edge edge)
        {
            switch (edge)
            {
                case Edge.Top:
                    return '┬';

                case Edge.Middle:
                    return '┼';

                case Edge.Bottom:
                    return '┴';

                default:
                    throw new ArgumentException("Invalid edge (left)", "edge");
            }
        }

        private static char RightCorner(Edge edge)
        {
            switch (edge)
            {
                case Edge.Top:
                    return '┐';

                case Edge.Middle:
                    return '┤';

                case Edge.Bottom:
                    return '┘';

                default:
                    throw new ArgumentException("Invalid edge (right)", "edge");
            }
        }
    }
}