using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.EntityBatch;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;
using static Bimwright.Dwg.Tests.McpSchemaAssertions;

namespace Bimwright.Dwg.Tests
{
    public class EntityBatchTests
    {
        private static JObject Point(double x, double y, double z = 0) => new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
        private static JObject Item(string id, string type) => new JObject { ["id"] = id, ["type"] = type, ["layer"] = "ЭОМ" };
        private static JObject Circle(string id = "boundary")
        { var o = Item(id, "circle"); o["center"] = Point(12, 34, 5); o["radius"] = 5; return o; }
        private static JObject Request(params JObject[] items) => new JObject { ["expected_document"] = @"D:\Drawings\electrical.dwg",
            ["expected_fingerprint"] = "1e3a3e15-4e07-c947-a816-9105ed7ec14d", ["entities"] = new JArray(items), ["dry_run"] = true };
        private static JObject Hatch(string id = "fill", string boundary = "boundary")
        { var o = Item(id, "hatch"); o["boundary_id"] = boundary; return o; }
        private static JObject Polyline(string id, int count = 4, bool closed = true)
        {
            var o = Item(id, "polyline"); var points = new JArray();
            for (int i = 0; i < count; i++) points.Add(Point(i, i % 2));
            o["points"] = points; o["closed"] = closed; return o;
        }

        [Fact]
        public void One_plan_supports_all_ten_types_with_prior_local_hatch_boundary()
        {
            var line = Item("wire", "line"); line["start"] = Point(0, 0); line["end"] = Point(30, 5);
            var point = Item("node", "point"); point["position"] = Point(10, 0);
            var rectangle = Item("box", "rectangle"); rectangle["position"] = Point(0, 0); rectangle["width"] = 50; rectangle["height"] = 20;
            var arc = Item("bend", "arc"); arc["center"] = Point(0, 0); arc["radius"] = 3; arc["start_angle"] = 15; arc["end_angle"] = 360;
            var ellipse = Item("ellipse", "ellipse"); ellipse["center"] = Point(0, 0); ellipse["major_radius"] = 10; ellipse["minor_radius"] = 2;
            var text = Item("label", "TEXT"); text["text"] = "ЩР-1"; text["position"] = Point(0, 0); text["text_style"] = "GOST TYPE A";
            var mtext = (JObject)text.DeepClone(); mtext["id"] = "note"; mtext["type"] = "mtext"; mtext["text"] = "Первая\\PВторая";
            var request = Request(line, Circle(), point, Polyline("path", 5, false), rectangle, arc, ellipse, text, mtext, Hatch());
            Assert.True(EntityBatchInput.TryParse(request, out var input, out var error), error);
            Assert.Equal(10, input.Items.Length); Assert.True(input.DryRun); Assert.Equal("text", input.Items[7].Type);
            Assert.Equal(5, (double)input.Items[1].Parameters["center"]["z"]);
            request["entities"][1]["center"]["z"] = 999;
            Assert.Equal(5, (double)input.Items[1].Parameters["center"]["z"]);
        }

        [Theory]
        [InlineData(849)] [InlineData(2000)]
        public void Complete_electrical_schematic_fits_one_plan(int count)
        {
            var request = Request(Enumerable.Range(0, count).Select(i => Circle("circle_" + i)).ToArray());
            Assert.True(EntityBatchInput.TryParse(request, out var input, out var error), error); Assert.Equal(count, input.Items.Length);
        }

        [Theory]
        [InlineData("relative")] [InlineData("fingerprint")] [InlineData("scope")] [InlineData("duplicate")]
        [InlineData("code")] [InlineData("unknown")] [InlineData("wrong_type")]
        [InlineData("nan")] [InlineData("radius")] [InlineData("color")]
        [InlineData("mixed_color")] [InlineData("weight")] [InlineData("id")] [InlineData("point_extra")]
        [InlineData("missing_layer")] [InlineData("too_many")]
        public void Unsafe_or_ambiguous_plan_is_rejected_before_any_gateway_access(string scenario)
        {
            var request = Request(Circle()); var entity = (JObject)request["entities"][0];
            switch (scenario)
            {
                case "relative": request["expected_document"] = "electrical.dwg"; break;
                case "fingerprint": request["expected_fingerprint"] = Guid.Empty.ToString(); break;
                case "scope": request["save"] = true; break;
                case "duplicate": ((JArray)request["entities"]).Add(Circle()); break;
                case "code": entity["type"] = "send_code"; break;
                case "unknown": entity["command"] = "ERASE ALL"; break;
                case "wrong_type": entity["width"] = 3; break;
                case "nan": entity["radius"] = double.NaN; break;
                case "radius": entity["radius"] = 0; break;
                case "color": entity["color_index"] = 3.5; break;
                case "mixed_color": entity["color_index"] = 1; entity["color_rgb"] = new JArray(1, 2, 3); break;
                case "weight": entity["lineweight_mm"] = 0.31; break;
                case "id": entity["id"] = "../file"; break;
                case "point_extra": entity["center"]["normal"] = 1; break;
                case "missing_layer": entity.Remove("layer"); break;
                case "too_many": request["entities"] = new JArray(Enumerable.Range(0, 2001).Select(i => Circle("c" + i))); break;
            }
            Assert.False(EntityBatchInput.TryParse(request, out _, out var error)); Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Theory]
        [InlineData("forward")] [InlineData("external")] [InlineData("self")] [InlineData("open")]
        [InlineData("crossed")] [InlineData("flat")] [InlineData("touching")]
        public void Hatch_dependencies_require_an_earlier_supported_simple_closed_boundary(string scenario)
        {
            JObject request;
            switch (scenario)
            {
                case "forward": request = Request(Hatch(), Circle()); break;
                case "external": request = Request(Circle(), Hatch(boundary: "1A2B")); break;
                case "self": request = Request(Hatch("fill", "fill")); break;
                case "open": request = Request(Polyline("boundary", 4, false), Hatch()); break;
                default:
                    var p = Polyline("boundary");
                    p["points"] = scenario == "crossed" ? new JArray(Point(0, 0), Point(10, 10), Point(0, 10), Point(10, 0)) :
                        scenario == "flat" ? new JArray(Point(0, 0), Point(5, 0), Point(10, 0)) :
                        new JArray(Point(0, 0), Point(10, 0), Point(5, 0), Point(5, 10));
                    request = Request(p, Hatch()); break;
            }
            Assert.False(EntityBatchInput.TryParse(request, out _, out var error)); Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void Concave_simple_hatch_boundary_is_supported_at_nonzero_elevation()
        {
            var p = Polyline("boundary"); p["points"] = new JArray(Point(0, 0, 8), Point(10, 0, 8), Point(10, 10, 8), Point(5, 5, 8), Point(0, 10, 8));
            Assert.True(EntityBatchInput.TryParse(Request(p, Hatch()), out _, out var error), error);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1000000000, 1)]
        [InlineData(1000000000000, 10)]
        [InlineData(-1000000000000, 10)]
        public void Hatch_area_validation_is_independent_of_large_WCS_offsets(double offset, double side)
        {
            // A genuine unit square previously became area=0 with absolute-WCS
            // shoelace products. The translation must not change its validity.
            // Keep the positive coordinates inside the documented 1e12 limit.
            double x = offset + side > 1e12 ? offset - side : offset;
            double y = offset + side > 1e12 ? offset - side : offset;
            var p = Polyline("boundary");
            p["points"] = new JArray(Point(x, y, 8), Point(x + side, y, 8), Point(x + side, y + side, 8), Point(x, y + side, 8));
            Assert.True(EntityBatchInput.TryParse(Request(p, Hatch()), out _, out var error), error);
        }

        [Fact]
        public void Translated_zero_area_hatch_boundary_is_still_rejected()
        {
            const double offset = 1000000000;
            var p = Polyline("boundary");
            p["points"] = new JArray(Point(offset, offset), Point(offset + 1, offset), Point(offset + 2, offset));
            Assert.False(EntityBatchInput.TryParse(Request(p, Hatch()), out _, out var error));
            Assert.Contains("zero area", error);
        }

        [Fact]
        public void Aggregate_vertex_budget_applies_across_small_polylines()
        {
            var request = Request(Enumerable.Range(0, 201).Select(i => Polyline("p" + i, 100, false)).ToArray());
            Assert.False(EntityBatchInput.TryParse(request, out _, out var error)); Assert.Contains("20000", error);
        }

        [Fact]
        public void Input_budget_counts_UTF8_bytes_including_Cyrillic_contents()
        {
            var label = Item("label", "mtext"); label["position"] = Point(0, 0); label["text_style"] = "Standard"; label["text"] = new string('Я', 16000);
            var request = Request(Enumerable.Range(0, 70).Select(i => { var o = (JObject)label.DeepClone(); o["id"] = "t" + i; return o; }).ToArray());
            Assert.False(EntityBatchInput.TryParse(request, out _, out var error)); Assert.Contains("2 MiB", error);
        }

        [Theory]
        [InlineData("field")] [InlineData("newline")] [InlineData("style")] [InlineData("multiplane")] [InlineData("zero_segment")]
        [InlineData("full_arc")]
        public void Unsupported_text_and_geometry_are_rejected_as_one_whole_plan(string scenario)
        {
            var label = Item("label", "text"); label["position"] = Point(0, 0); label["text_style"] = "Standard"; label["text"] = "Label";
            var request = Request(Circle(), label);
            switch (scenario)
            {
                case "field": label["text"] = "%<\\AcExpr 1+1>%"; request = Request(Circle(), label); break;
                case "newline": request["entities"][1]["text"] = "one\ntwo"; break;
                case "style": ((JObject)request["entities"][1]).Remove("text_style"); break;
                case "multiplane": var p = Polyline("path", 4, false); p["points"][2]["z"] = 1; request = Request(Circle(), p); break;
                case "zero_segment": var q = Polyline("path", 4, false); q["points"][2] = q["points"][1].DeepClone(); request = Request(Circle(), q); break;
                case "full_arc": var a = Item("arc", "arc"); a["center"] = Point(0, 0); a["radius"] = 2; a["start_angle"] = 0; a["end_angle"] = 360; request = Request(a); break;
            }
            Assert.False(EntityBatchInput.TryParse(request, out _, out var error)); Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Theory]
        [InlineData(false, 1)] [InlineData(true, 0)] [InlineData(true, 3)] [InlineData(true, 9)]
        public void Unsupported_Undo_state_is_refused_without_changing_system_variables(bool recording, int control)
            => Assert.NotNull(EntityBatchInput.ValidateUndoState(recording, control));
        [Fact] public void Normal_Undo_state_does_not_require_a_command_string() => Assert.Null(EntityBatchInput.ValidateUndoState(true, 53));

        [Fact]
        public void Actual_MCP_SDK_exposes_typed_items_and_nonidempotent_write_annotations()
        {
            var tool = McpServerTool.Create(typeof(EntityBatchTools).GetMethod(nameof(EntityBatchTools.CreateEntities)), (object)null).ProtocolTool;
            Assert.False(tool.Annotations.ReadOnlyHint); Assert.False(tool.Annotations.IdempotentHint);
            var required = tool.InputSchema.GetProperty("required").EnumerateArray().Select(p => p.GetString());
            Assert.Contains("expected_document", required); Assert.Contains("expected_fingerprint", required); Assert.Contains("entities", required);
            var entities = tool.InputSchema.GetProperty("properties").GetProperty("entities");
            Assert.Equal("array", entities.GetProperty("type").GetString());
            var item = entities.GetProperty("items");
            Assert.Equal("object", item.GetProperty("type").GetString());
            Assert.Contains("id", item.GetProperty("required").EnumerateArray().Select(p => p.GetString()));
            var position = item.GetProperty("properties").GetProperty("position");
            AssertSchemaType(position, "object", allowNull: true);
            foreach (var coordinate in new[] { "x", "y", "z" })
                AssertSchemaType(position.GetProperty("properties").GetProperty(coordinate), "number");
            var requiredCoordinates = position.GetProperty("required").EnumerateArray().Select(p => p.GetString());
            Assert.Contains("x", requiredCoordinates);
            Assert.Contains("y", requiredCoordinates);
        }

        [Fact]
        public void Read_only_profile_never_registers_batch_writes()
        {
            var method = typeof(Bimwright.Dwg.Server.Program).GetMethod("ResolveToolTypesForRegistration", BindingFlags.NonPublic | BindingFlags.Static);
            var types = (IEnumerable<Type>)method.Invoke(null, new object[] { new HashSet<string>(new[] { "modify", "drawing", "query" }, StringComparer.OrdinalIgnoreCase), true });
            Assert.DoesNotContain(typeof(EntityBatchTools), types);
        }
        [Fact]
        public async Task Invalid_facade_plan_fails_locally_before_any_native_call()
        {
            var result = JObject.Parse(await EntityBatchTools.CreateEntities("relative.dwg", Guid.NewGuid().ToString(),
                new[] { new EntityBatchEntity { id = "x", type = "circle", layer = "0", center = new EntityBatchPoint(), radius = 3 } }));
            Assert.False(result.Value<bool>("ok")); Assert.Contains("fully qualified", (string)result["error"]);
        }
    }
}
