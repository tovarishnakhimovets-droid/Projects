using System;
using Bimwright.Dwg.Plugin.Rewriting;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class RewriteActionNamesTests
    {
        [Theory]
        [InlineData(RewriteAction.Update, "update")]
        [InlineData(RewriteAction.Collapse, "collapse")]
        [InlineData(RewriteAction.RewriteInBlock, "rewrite_in_block")]
        public void Neutral_actions_have_stable_wire_names(RewriteAction action, string expected)
        { Assert.Equal(expected, RewriteActionNames.ToWire(action)); }

        [Fact]
        public void There_are_only_three_supported_actions()
        { Assert.Equal(3, Enum.GetValues(typeof(RewriteAction)).Length); }

        [Fact]
        public void Unsupported_action_is_refused()
        { Assert.Throws<ArgumentOutOfRangeException>(() => RewriteActionNames.ToWire((RewriteAction)999)); }
    }
}
