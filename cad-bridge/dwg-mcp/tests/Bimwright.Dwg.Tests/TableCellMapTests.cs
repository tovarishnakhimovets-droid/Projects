using System;
using Bimwright.Dwg.Plugin.Tables;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class TableCellMapTests
    {
        [Theory]
        [InlineData(0, "top_to_bottom")]
        [InlineData(1, "bottom_to_top")]
        public void Table_flow_uses_the_native_TABLE_domain(int nativeValue, string expected)
            => Assert.Equal(expected, TableFlowDirectionValue.Name(nativeValue));

        [Theory]
        [InlineData(-1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void MText_and_unknown_flow_codes_are_refused(int value)
            => Assert.Throws<ArgumentException>(() => TableFlowDirectionValue.Name(value));

        [Fact]
        public void Native_anchor_ranges_cover_nonanchors_even_when_their_native_IsMerged_is_false()
        {
            var map = new TableCellMap(4, 4, new[] { new TableMerge(0,0,0,3), new TableMerge(1,3,2,3) });
            Assert.False(map.IsNonAnchor(0,0));
            for (int column = 1; column < 4; column++)
            {
                Assert.True(map.IsNonAnchor(0,column));
                Assert.Equal(0,map.AnchorRow(0,column)); Assert.Equal(0,map.AnchorColumn(0,column));
            }
            Assert.False(map.IsNonAnchor(1,3)); Assert.True(map.IsNonAnchor(2,3));
            Assert.Equal(1,map.AnchorRow(2,3)); Assert.Equal(3,map.AnchorColumn(2,3));
            // An ordinary empty cell still owns its independent native formatting.
            Assert.False(map.IsNonAnchor(1,2));
            Assert.Equal(1,map.AnchorRow(1,2)); Assert.Equal(2,map.AnchorColumn(1,2));
        }

        [Theory]
        [InlineData(-1,0,0,1)] [InlineData(0,-1,0,1)] [InlineData(0,0,4,1)]
        [InlineData(0,0,0,4)] [InlineData(2,0,1,1)] [InlineData(0,2,1,1)] [InlineData(0,0,0,0)]
        public void Invalid_native_ranges_do_not_produce_a_complete_readback(int top,int left,int bottom,int right)
            => Assert.Throws<ArgumentException>(() => new TableCellMap(4,4,new[] {new TableMerge(top,left,bottom,right)}));

        [Fact]
        public void Overlap_and_duplicate_native_ranges_are_refused()
        {
            Assert.Throws<ArgumentException>(() => new TableCellMap(4,4,new[] {new TableMerge(0,0,1,1),new TableMerge(1,1,2,2)}));
            Assert.Throws<ArgumentException>(() => new TableCellMap(4,4,new[] {new TableMerge(0,0,1,1),new TableMerge(0,0,1,1)}));
        }

        [Theory]
        [InlineData(0,1)] [InlineData(1,0)] [InlineData(10001,1)]
        public void Invalid_shapes_are_refused(int rows,int columns)
            => Assert.Throws<ArgumentException>(() => new TableCellMap(rows,columns,Array.Empty<TableMerge>()));

        [Theory]
        [InlineData(-1,0)] [InlineData(0,-1)] [InlineData(2,0)] [InlineData(0,3)]
        [InlineData(int.MaxValue,int.MaxValue)]
        public void Invalid_coordinates_do_not_alias_another_native_cell(int row,int column)
        {
            var map=new TableCellMap(2,3,Array.Empty<TableMerge>());
            Assert.Throws<ArgumentOutOfRangeException>(() => map.IsNonAnchor(row,column));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.AnchorRow(row,column));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.AnchorColumn(row,column));
        }

        [Fact]
        public void A_two_dimensional_merge_keeps_its_nonzero_anchor()
        {
            var map=new TableCellMap(4,4,new[] {new TableMerge(1,1,2,2)});
            Assert.False(map.IsNonAnchor(1,1));
            foreach(var position in new[] {(1,2),(2,1),(2,2)})
            {
                Assert.True(map.IsNonAnchor(position.Item1,position.Item2));
                Assert.Equal(1,map.AnchorRow(position.Item1,position.Item2));
                Assert.Equal(1,map.AnchorColumn(position.Item1,position.Item2));
            }
        }

        [Fact]
        public void Cell_budget_is_checked_before_allocating_or_multiplying_ints()
        {
            var map=new TableCellMap(1,10000,Array.Empty<TableMerge>());
            Assert.Equal(9999,map.AnchorColumn(0,9999));
            Assert.Throws<ArgumentException>(() => new TableCellMap(100000,100000,Array.Empty<TableMerge>()));
        }

        [Fact]
        public void Null_native_ranges_are_refused()
        {
            Assert.Throws<ArgumentNullException>(() => new TableCellMap(2,2,null));
            Assert.Throws<ArgumentException>(() => new TableCellMap(2,2,new TableMerge[] {null}));
        }

        [Fact]
        public void Unmerged_cells_keep_their_own_coordinates()
        {
            var map=new TableCellMap(2,3,Array.Empty<TableMerge>());
            for(int row=0;row<2;row++) for(int column=0;column<3;column++)
            {
                Assert.False(map.IsNonAnchor(row,column));
                Assert.Equal(row,map.AnchorRow(row,column)); Assert.Equal(column,map.AnchorColumn(row,column));
            }
        }
    }
}
