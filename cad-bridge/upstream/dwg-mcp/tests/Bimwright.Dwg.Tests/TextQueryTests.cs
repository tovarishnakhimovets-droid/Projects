using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class TextQueryTests
    {
        [Theory]
        [InlineData(null, 100)]
        [InlineData("null", 100)]
        [InlineData("{}", 100)]
        [InlineData("{\"limit\":null}", 100)]
        [InlineData("{\"limit\":1}", 1)]
        [InlineData("{\"limit\":1000}", 1000)]
        public void Shared_input_accepts_default_and_bounded_integer_limits(string json, int expectedLimit)
        {
            Assert.True(ListTextStylesInput.TryParse(Parse(json), out var input, out var error), error);
            Assert.Equal(expectedLimit, input.Limit);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("1")]
        [InlineData("{\"limit\":0}")]
        [InlineData("{\"limit\":1001}")]
        [InlineData("{\"limit\":1.0}")]
        [InlineData("{\"limit\":\"10\"}")]
        [InlineData("{\"limit\":true}")]
        [InlineData("{\"limit\":999999999999999999999}")]
        public void Shared_input_rejects_coercions_overflow_and_out_of_range_before_host_access(string json)
        {
            Assert.False(ListTextStylesInput.TryParse(Parse(json), out _, out var error));
            Assert.False(string.IsNullOrWhiteSpace(error));
            var doc = new Document { DatabaseFactory = () => throw new Exception("unexpected host access") };
            var result = new ListTextStylesHandler().Execute(doc, Parse(json));
            Assert.False(result.Ok);
            Assert.Equal(error, result.Error);
        }

        [Fact]
        public void Actual_MCP_query_schema_has_optional_integer_limit_and_read_only_metadata()
        {
            var tool = McpServerTool.Create(typeof(TextQueryTools).GetMethod(nameof(TextQueryTools.ListTextStyles)), (object)null).ProtocolTool;
            Assert.Equal("dwg_list_text_styles", tool.Name);
            Assert.True(tool.Annotations.ReadOnlyHint);
            Assert.True(tool.Annotations.IdempotentHint);
            Assert.Equal("integer", tool.InputSchema.GetProperty("properties").GetProperty("limit").GetProperty("type").GetString());
            if (tool.InputSchema.TryGetProperty("required", out var required))
                Assert.Empty(required.EnumerateArray());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1001)]
        public async Task Invalid_typed_requests_return_errors_without_contacting_CAD(int limit)
        {
            var result = JObject.Parse(await TextQueryTools.ListTextStyles(limit));
            Assert.False((bool?)result["ok"]);
            Assert.Contains("limit", result.Value<string>("error"));
        }

        [Fact]
        public void Native_schema_accepts_omitted_limit_and_rejects_non_integer_tokens_and_array_root()
        {
            Assert.True(SchemaValidator.Validate("list_text_styles", null, CommandSchemas.ListTextStyles).Ok);
            Assert.True(SchemaValidator.Validate("list_text_styles", JObject.Parse("{\"limit\":1000}"), CommandSchemas.ListTextStyles).Ok);
            foreach (var json in new[] { "[]", "{\"limit\":1.5}", "{\"limit\":\"5\"}", "{\"limit\":true}" })
                Assert.False(SchemaValidator.Validate("list_text_styles", JToken.Parse(json), CommandSchemas.ListTextStyles).Ok);
        }

        [Theory]
        [InlineData(0, 2, false)]
        [InlineData(2, 2, false)]
        [InlineData(3, 2, true)]
        public void Real_handler_caps_records_detects_overflow_and_opens_only_returned_styles_for_read(int total, int limit, bool truncated)
        {
            var doc = CreateDocument(total);
            var result = new ListTextStylesHandler().Execute(doc, new JObject { ["limit"] = limit });
            Assert.True(result.Ok, result.Error);
            var json = JObject.FromObject(result.Result);
            Assert.Equal(Math.Min(total, limit), json.Value<int>("count"));
            Assert.Equal(limit, json.Value<int>("limit"));
            Assert.Equal(truncated, json.Value<bool>("truncated"));
            Assert.Equal(Math.Min(total, limit), ((JArray)json["styles"]).Count);
            var manager = doc.Database.TransactionManager;
            Assert.Equal(Math.Min(total, limit) + 1, manager.Opens.Count); // table plus returned records
            Assert.All(manager.Opens, entry => Assert.Equal(OpenMode.ForRead, entry.Mode));
            Assert.Equal(1, manager.CommitCount);
            if (total > 0)
            {
                Assert.True(json["styles"][0].Value<bool>("is_current"));
                Assert.Equal("Style0", json["styles"][0].Value<string>("name"));
            }
        }

        [Fact]
        public void Metadata_copies_recorded_font_scalars_and_preserves_entity_specific_width_and_oblique()
        {
            var doc = CreateDocument(1);
            var styleId = doc.Database.Textstyle;
            var style = (TextStyleTableRecord)doc.Database.TransactionManager.Objects[styleId];
            var result = new Dictionary<string, object> { ["width_factor"] = 0.6d, ["oblique"] = 0.2d };
            using (var tx = doc.Database.TransactionManager.StartTransaction())
                CadTextStyleMetadata.AddToEntity(result, styleId, tx);
            style.Name = "later change";
            style.Font = new FontDescriptor("later face", false, true, 0, 0);
            var json = JObject.FromObject(result);
            Assert.Equal("Style0", json.Value<string>("style_name"));
            Assert.Equal("2", json.Value<string>("style_handle"));
            Assert.Equal("example.ttf", json.Value<string>("font_file"));
            Assert.Equal("bigfont.shx", json.Value<string>("big_font_file"));
            Assert.Equal("Example Typeface", json.Value<string>("typeface"));
            Assert.True(json.Value<bool>("bold"));
            Assert.False(json.Value<bool>("italic"));
            Assert.Equal(204, json.Value<int>("character_set"));
            Assert.Equal(34, json.Value<int>("pitch_and_family"));
            Assert.Equal(0.6d, json.Value<double>("width_factor"));
            Assert.Equal(0.2d, json.Value<double>("oblique"));
            Assert.Equal(0.85d, json.Value<double>("style_width_factor"));
            Assert.Equal(0.1d, json.Value<double>("style_oblique"));
        }

        [Fact]
        public void Unavailable_font_descriptor_preserves_SHX_metadata_and_reports_null_descriptor_fields()
        {
            var doc = CreateDocument(1);
            var style = (TextStyleTableRecord)doc.Database.TransactionManager.Objects[doc.Database.Textstyle];
            style.FileName = "simplex.shx";
            style.FontReadError = new InvalidOperationException("font descriptor unavailable");
            var json = JObject.FromObject(CadTextStyleMetadata.Describe(style));
            Assert.Equal("simplex.shx", json.Value<string>("font_file"));
            Assert.Equal("bigfont.shx", json.Value<string>("big_font_file"));
            Assert.Equal(JTokenType.Null, json["typeface"].Type);
            Assert.Equal(JTokenType.Null, json["bold"].Type);
            Assert.Contains("unavailable", json.Value<string>("font_metadata_error"));
        }

        [Fact]
        public void Missing_style_does_not_hide_existing_entity_properties()
        {
            var result = new Dictionary<string, object> { ["text"] = "Label", ["height"] = 2.5d };
            CadTextStyleMetadata.AddToEntity(result, ObjectId.Null, null);
            Assert.Equal("Label", result["text"]);
            Assert.Equal(2.5d, result["height"]);
            Assert.Null(result["style_name"]);
            Assert.NotNull(result["style_metadata_error"]);
        }

        private static JToken Parse(string json) => json == null ? null : JToken.Parse(json);

        private static Document CreateDocument(int styleCount)
        {
            var doc = new Document();
            var db = doc.Database;
            var table = new TextStyleTable();
            db.TransactionManager.Objects.Add(db.TextStyleTableId, table);
            for (var i = 0; i < styleCount; i++)
            {
                var id = new ObjectId(i + 2);
                table.Ids.Add(id);
                db.TransactionManager.Objects.Add(id, new TextStyleTableRecord
                {
                    Name = "Style" + i,
                    Handle = new Handle(i + 2),
                    FileName = "example.ttf",
                    BigFontFileName = "bigfont.shx",
                    XScale = 0.85d,
                    ObliquingAngle = 0.1d,
                    TextSize = 2.5d,
                    Font = new FontDescriptor("Example Typeface", true, false, 204, 34)
                });
            }
            db.Textstyle = styleCount > 0 ? table.Ids[0] : ObjectId.Null;
            return doc;
        }
    }
}
