using System;
using System.Linq;
using Bimwright.Dwg.Plugin.Geometry;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class PolylineVertexEditTests
    {
        [Fact]
        public void Moving_an_index_preserves_curves_tapered_widths_and_metadata_without_mutating_snapshot()
        {
            var wire = Request();
            wire["changes"][0]["expected_is_closed"] = true;
            var input = Parse(wire);
            var change = input.Changes.Single();
            var result = PolylineVertexEditRequest.Plan(change, change.Expected, input.MinSegmentLength);
            Assert.Equal(15, result.Vertices[1].X);
            Assert.Equal(1, result.Vertices[1].Y);
            Assert.Equal(10, change.Expected.Vertices[1].X);
            Assert.Equal(0, change.Expected.Vertices[1].Y);
            Assert.True(result.Closed);
            Assert.Equal(change.Expected.Normal, result.Normal);
            Assert.Equal(change.Expected.Elevation, result.Elevation);
            Assert.Equal(change.Expected.Thickness, result.Thickness);
            Assert.Equal(change.Expected.ConstantWidth, result.ConstantWidth);
            Assert.Equal(change.Expected.OwnerHandle, result.OwnerHandle);
            Assert.Equal(change.Expected.Layer, result.Layer);
            for (int index = 0; index < result.Vertices.Length; index++)
            {
                Assert.Equal(change.Expected.Vertices[index].Bulge, result.Vertices[index].Bulge);
                Assert.Equal(change.Expected.Vertices[index].StartWidth, result.Vertices[index].StartWidth);
                Assert.Equal(change.Expected.Vertices[index].EndWidth, result.Vertices[index].EndWidth);
                if (index != 1) Assert.True(change.Expected.Vertices[index].Same(result.Vertices[index]));
            }
        }

        [Theory]
        [InlineData("x")]
        [InlineData("bulge")]
        [InlineData("start_width")]
        [InlineData("end_width")]
        [InlineData("layer")]
        [InlineData("owner")]
        [InlineData("normal")]
        [InlineData("elevation")]
        [InlineData("thickness")]
        [InlineData("constant_width")]
        [InlineData("closed")]
        public void Every_snapshot_component_is_guarded_exactly(string field)
        {
            var input = Parse(Request());
            var actual = Parse(Request()).Changes[0].Expected;
            switch (field)
            {
                case "x": actual.Vertices[1].X = Math.BitIncrement(actual.Vertices[1].X); break;
                case "bulge": actual.Vertices[0].Bulge = Math.BitIncrement(actual.Vertices[0].Bulge); break;
                case "start_width": actual.Vertices[0].StartWidth += 0.001; break;
                case "end_width": actual.Vertices[0].EndWidth += 0.001; break;
                case "layer": actual.Layer = "Other"; break;
                case "owner": actual.OwnerHandle = "BB"; break;
                case "normal": actual.Normal[0] = 1e-15; break;
                case "elevation": actual.Elevation = Math.BitIncrement(actual.Elevation); break;
                case "thickness": actual.Thickness = 1; break;
                case "constant_width": actual.ConstantWidth = 1; break;
                case "closed": actual.Closed = true; break;
            }
            Assert.Throws<ArgumentException>(() => PolylineVertexEditRequest.Plan(input.Changes[0], actual, input.MinSegmentLength));
        }

        [Theory]
        [InlineData("x")]
        [InlineData("index")]
        [InlineData("bulge")]
        [InlineData("start_width")]
        [InlineData("end_width")]
        public void Missing_vertex_snapshot_fields_are_not_silently_defaulted(string field)
        {
            var wire = Request();
            ((JObject)wire["changes"][0]["expected_vertices"][0]).Remove(field);
            Assert.False(PolylineVertexEditRequest.TryParse(wire, out _, out var error));
            Assert.Contains(field, error);
        }

        [Theory]
        [InlineData("expected_document", "relative.dwg")]
        [InlineData("expected_document", "D:\\template.dwt")]
        [InlineData("expected_fingerprint", "bad")]
        [InlineData("dry_run", "true")]
        public void Invalid_document_identity_or_option_types_are_rejected(string field, string value)
        {
            var wire = Request();
            wire[field] = value;
            Assert.False(PolylineVertexEditRequest.TryParse(wire, out _, out var error));
            Assert.Contains(field, error);
        }

        [Fact]
        public void All_geometry_is_finite_and_bounded()
        {
            foreach (double number in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e13 })
            {
                var wire = Request();
                wire["changes"][0]["edits"][0]["position"][0] = number;
                Assert.False(PolylineVertexEditRequest.TryParse(wire, out _, out _));
            }
        }

        [Fact]
        public void Planar_scope_and_elevation_are_exact()
        {
            var wire = Request();
            wire["changes"][0]["expected_normal"] = new JArray(0d, 0d, -1d);
            Assert.False(PolylineVertexEditRequest.TryParse(wire, out _, out _));
            foreach (double elevation in new[] { Math.BitDecrement(7d), Math.BitIncrement(7d) })
            {
                wire = Request();
                // Replacing a JValue can ignore adjacent doubles as approximately equal.
                // A new array retains the exact input needed to exercise the planar guard.
                wire["changes"][0]["edits"][0]["position"] = new JArray(15d, 1d, elevation);
                Assert.Equal(elevation, wire["changes"][0]["edits"][0]["position"][2].Value<double>());
                Assert.NotEqual(7d, elevation);
                Assert.False(PolylineVertexEditRequest.TryParse(wire, out _, out var error));
                Assert.Contains("exact existing elevation", error);
            }
        }

        [Fact]
        public void Duplicate_handles_indices_and_out_of_range_indices_are_rejected()
        {
            var wire = Request();
            var duplicate = wire["changes"][0].DeepClone();
            duplicate["handle"] = "00a";
            ((JArray)wire["changes"]).Add(duplicate);
            Assert.False(PolylineVertexEditRequest.TryParse(wire, out _, out var error));
            Assert.Contains("duplicate polyline", error);
            wire = Request();
            var edits = (JArray)wire["changes"][0]["edits"];
            edits.Add(edits[0].DeepClone());
            Assert.False(PolylineVertexEditRequest.TryParse(wire, out _, out _));
            foreach (var index in new JToken[] { -1, 3, 1.5, "1" })
            {
                wire = Request();
                wire["changes"][0]["edits"][0]["index"] = index;
                Assert.False(PolylineVertexEditRequest.TryParse(wire, out _, out _));
            }
        }

        [Fact]
        public void Closing_chord_is_checked_and_bulged_zero_chords_are_refused()
        {
            var wire = Request();
            wire["changes"][0]["expected_is_closed"] = true;
            wire["changes"][0]["edits"][0] = new JObject { ["index"] = 2, ["position"] = new JArray(0d, 0d, 7d) };
            Assert.False(PolylineVertexEditRequest.TryParse(wire, out _, out var error));
            Assert.Contains("segment chord", error);
        }

        [Fact]
        public void Aggregate_vertex_budget_is_enforced_across_all_polylines()
        {
            var wire = Request();
            var first = (JObject)wire["changes"][0];
            first["expected_vertices"] = new JArray(Enumerable.Range(0, 10001).Select(index => Vertex(index, index, 0)));
            var second = first.DeepClone();
            second["handle"] = "B";
            ((JArray)wire["changes"]).Add(second);
            Assert.False(PolylineVertexEditRequest.TryParse(wire, out _, out var error));
            Assert.Contains("20000", error);
        }

        internal static PolylineVertexEditRequest Parse(JObject wire)
        {
            Assert.True(PolylineVertexEditRequest.TryParse(wire, out var request, out var error), error);
            return request;
        }
        internal static JObject Request() => new JObject
        {
            ["expected_document"] = @"D:\drawings\test.dwg", ["expected_fingerprint"] = "11223344-1122-3344-5566-778899aabbcc",
            ["changes"] = new JArray(new JObject
            {
                ["handle"] = "A", ["expected_layer"] = "Network", ["expected_owner_handle"] = "1F",
                ["expected_vertices"] = new JArray(Vertex(0, 0, 0), Vertex(1, 10, 0), Vertex(2, 10, 10)),
                ["expected_normal"] = new JArray(0d, 0d, 1d), ["expected_elevation"] = 7d,
                ["expected_thickness"] = -2d, ["expected_constant_width"] = 0d, ["expected_is_closed"] = false,
                ["edits"] = new JArray(new JObject { ["index"] = 1, ["position"] = new JArray(15d, 1d, 7d) })
            })
        };
        private static JObject Vertex(int index, double x, double y) => new JObject
        {
            ["index"] = index, ["x"] = x, ["y"] = y, ["z"] = 7d,
            ["bulge"] = index == 0 ? 0.4142135623730951 : -0.2, ["start_width"] = 0.2, ["end_width"] = 0.5
        };
    }
}
