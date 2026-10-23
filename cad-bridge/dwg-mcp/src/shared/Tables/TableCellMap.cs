using System;
using System.Collections.Generic;

namespace Bimwright.Dwg.Plugin.Tables
{
    // Native TABLE uses AcDb::FlowDirection (0/1), unlike the managed MText enum (3/4).
    internal static class TableFlowDirectionValue
    {
        internal const int TopToBottom = 0;
        internal const int BottomToTop = 1;
        internal static string Name(int value)
        {
            if (value == TopToBottom) return "top_to_bottom";
            if (value == BottomToTop) return "bottom_to_top";
            throw new ArgumentException("unsupported native TABLE flow direction: " + value);
        }
    }

    // Native merged non-anchors have no independent appearance, even when IsMerged is false.
    internal sealed class TableCellMap
    {
        private readonly int rows;
        private readonly int columns;
        private readonly int[] anchors;

        internal TableCellMap(int rows, int columns, IEnumerable<TableMerge> merges)
        {
            if (rows <= 0 || columns <= 0 || (long)rows * columns > TableInput.MaxCells)
                throw new ArgumentException("invalid table shape for native merge coverage");
            if (merges == null) throw new ArgumentNullException(nameof(merges));
            this.rows = rows;
            this.columns = columns;
            anchors = new int[rows * columns];
            var covered = new bool[anchors.Length];
            for (int i = 0; i < anchors.Length; i++) anchors[i] = i;
            foreach (var merge in merges)
            {
                if (merge == null || merge.Top < 0 || merge.Left < 0 || merge.Bottom >= rows ||
                    merge.Right >= columns || merge.Bottom < merge.Top || merge.Right < merge.Left ||
                    (merge.Top == merge.Bottom && merge.Left == merge.Right))
                    throw new ArgumentException("native merged range is outside the table or has fewer than two cells");
                int anchor = merge.Top * columns + merge.Left;
                for (int row = merge.Top; row <= merge.Bottom; row++)
                    for (int column = merge.Left; column <= merge.Right; column++)
                    {
                        int index = row * columns + column;
                        if (covered[index]) throw new ArgumentException("native merged ranges overlap");
                        covered[index] = true;
                        anchors[index] = anchor;
                    }
            }
        }

        private int Index(int row, int column)
        {
            if (row < 0 || row >= rows) throw new ArgumentOutOfRangeException(nameof(row));
            if (column < 0 || column >= columns) throw new ArgumentOutOfRangeException(nameof(column));
            return row * columns + column;
        }
        internal bool IsNonAnchor(int row, int column) => anchors[Index(row, column)] != Index(row, column);
        internal int AnchorRow(int row, int column) => anchors[Index(row, column)] / columns;
        internal int AnchorColumn(int row, int column) => anchors[Index(row, column)] % columns;
    }
}
