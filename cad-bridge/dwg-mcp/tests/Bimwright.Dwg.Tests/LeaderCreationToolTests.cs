using System;
using System.Linq;
using System.Reflection;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class LeaderCreationToolTests
    {
        [Fact]
        public void Leader_has_one_MCP_registration_and_only_a_legacy_forwarder()
        {
            var methods = typeof(QueryTools).Assembly.GetTypes().SelectMany(type => type.GetMethods());
            var registrations = methods.Where(method =>
                method.GetCustomAttribute<McpServerToolAttribute>()?.Name == "dwg_create_leader").ToArray();
            Assert.Single(registrations);
            Assert.Equal(typeof(TextCreationTools), registrations[0].DeclaringType);
            var compatibility = typeof(AnnotationTools).GetMethod(nameof(AnnotationTools.CreateLeader));
            Assert.NotNull(compatibility);
            Assert.Null(compatibility.GetCustomAttribute<McpServerToolAttribute>());
        }

        [Fact]
        public void SDK_schema_advertises_one_straight_leader_with_explicit_sizes()
        {
            var tool = McpServerTool.Create(typeof(TextCreationTools).GetMethod(nameof(TextCreationTools.CreateLeader)),
                (object)null).ProtocolTool;
            Assert.Equal("dwg_create_leader", tool.Name);
            Assert.False(tool.Annotations.ReadOnlyHint);
            Assert.False(tool.Annotations.IdempotentHint);
            Assert.Equal(new[] { "points" }, tool.InputSchema.GetProperty("required").EnumerateArray()
                .Select(value => value.GetString()).ToArray());
            Assert.Equal(new[] { "arrow_size", "color_index", "height", "layer", "points", "text", "text_style" },
                tool.InputSchema.GetProperty("properties").EnumerateObject().Select(value => value.Name)
                    .OrderBy(value => value, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void Request_builder_preserves_the_two_WCS_points_and_appearance()
        {
            Assert.True(TextCreationTools.TryBuildLeaderRequest(
                "[{\"x\":1,\"y\":2,\"z\":3},{\"x\":4,\"y\":5,\"z\":3}]", "Колодец",
                "НВ", 150, 2.2, 1.5, "GOST TYPE A", out var request, out var error), error);
            Assert.Equal(2, request["points"].Count());
            Assert.Equal(150, request.Value<int>("color_index"));
            Assert.Equal(2.2, request.Value<double>("height"));
            Assert.Equal(1.5, request.Value<double>("arrow_size"));
            Assert.Equal("GOST TYPE A", request.Value<string>("text_style"));
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData("[]")]
        [InlineData("[{\"x\":0,\"y\":0}]")]
        [InlineData("[{\"x\":0,\"y\":0},{\"x\":0,\"y\":0}]")]
        public void Invalid_request_is_refused_without_a_CAD_call(string points)
        {
            Assert.False(TextCreationTools.TryBuildLeaderRequest(points, "Text", null, null,
                null, null, null, out var request, out var error));
            Assert.Null(request);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }
}
