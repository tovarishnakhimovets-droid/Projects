using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class TextPropertyToolTests
    {
        [Theory]
        [InlineData("1", "01")]
        [InlineData("AB", "ab")]
        [InlineData("F", "000F")]
        public void Numeric_handle_aliases_refuse_whole_set(string first, string second)
        {
            var request = new JObject { ["handles"] = new JArray(first, second), ["text_style"] = "GOST TYPE A" };
            Assert.False(PropertyStyleInput.TrySet(request, out _, out _, out var error));
            Assert.Contains("duplicate", error);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-A")]
        [InlineData("+A")]
        [InlineData(" A")]
        [InlineData("G")]
        [InlineData("FFFFFFFFFFFFFFFF")]
        public void Malformed_or_unresolvable_handle_shape_refused(string handle)
        {
            Assert.False(PropertyStyleInput.TryHandle(handle, out _, out _));
        }

        [Fact]
        public void Bound_applies_before_contacting_native_host()
        {
            var handles = new JArray(Enumerable.Range(1,1000).Select(i => i.ToString("X")));
            Assert.True(PropertyStyleInput.TryHandles(handles,"handles",out var parsed,out var error), error);
            Assert.Equal(1000,parsed.Length);
            handles.Add("FFFF");
            Assert.False(PropertyStyleInput.TryHandles(handles,"handles",out _,out error));
            Assert.Contains("1000",error);
        }

        [Theory]
        [InlineData("text")]
        [InlineData("position")]
        [InlineData("rotation")]
        [InlineData("all")]
        [InlineData("Text_Style")]
        public void Content_and_geometry_are_not_matchable_properties(string property)
        {
            Assert.False(PropertyStyleInput.TryMatch(Match(property),out _,out _,out _,out var error));
            Assert.Contains("unsupported",error);
        }

        [Fact]
        public void Match_refuses_source_alias_and_duplicate_properties()
        {
            var input = Match("text_style");
            input["target_handles"] = new JArray("01");
            Assert.False(PropertyStyleInput.TryMatch(input,out _,out _,out _,out var error));
            Assert.Contains("source",error);
            input["target_handles"] = new JArray("2");
            input["properties"] = new JArray("color","color");
            Assert.False(PropertyStyleInput.TryMatch(input,out _,out _,out _,out error));
            Assert.Contains("duplicate",error);
        }

        [Fact]
        public void Style_name_and_requested_properties_are_not_normalized()
        {
            var input = new JObject { ["handles"] = new JArray("1A"), ["text_style"] = "GOST TYPE A" };
            Assert.True(PropertyStyleInput.TrySet(input,out _,out var name,out var error),error);
            Assert.Equal("GOST TYPE A",name);
            Assert.True(PropertyStyleInput.TryMatch(Match("layer","text_style"),out _,out _,out var properties,out error),error);
            Assert.Equal(new[] { "layer","text_style" },properties);
        }

        [Theory]
        [InlineData("SetTextStyle","dwg_set_text_style","handles,text_style")]
        [InlineData("MatchProperties","dwg_match_properties","properties,source_handle,target_handles")]
        public void Actual_SDK_schema_has_only_required_scalar_inputs(string method,string name,string required)
        {
            var tool = McpServerTool.Create(typeof(EntityStyleTools).GetMethod(method),(object)null).ProtocolTool;
            Assert.Equal(name,tool.Name);
            Assert.False(tool.Annotations.ReadOnlyHint);
            Assert.True(tool.Annotations.IdempotentHint);
            var schema = tool.InputSchema;
            Assert.Equal(required.Split(','),schema.GetProperty("required").EnumerateArray().Select(p=>p.GetString()).OrderBy(p=>p));
            foreach(var field in method=="SetTextStyle" ? new[] {"handles"} : new[] {"target_handles","properties"})
            {
                var array=schema.GetProperty("properties").GetProperty(field);
                Assert.Equal("array",array.GetProperty("type").GetString());
                Assert.Equal("string",array.GetProperty("items").GetProperty("type").GetString());
            }
        }

        [Fact]
        public async Task Invalid_facades_refuse_before_host_connection()
        {
            foreach(var call in new[] {
                EntityStyleTools.SetTextStyle(new[] {"A","0A"},"GOST TYPE A"),
                EntityStyleTools.SetTextStyle(new[] {"A"}," "),
                EntityStyleTools.MatchProperties("1",new[] {"2"},new[] {"position"}),
                EntityStyleTools.MatchProperties("1",new[] {"01"},new[] {"text_style"}) })
            {
                var result=JObject.Parse(await call);
                Assert.False(result.Value<bool>("ok"));
                Assert.False(string.IsNullOrWhiteSpace(result.Value<string>("error")));
            }
        }

        [Fact]
        public void Wire_schema_refuses_wrong_token_shapes()
        {
            Assert.True(SchemaValidator.Validate("match_properties",Match("text_style"),CommandSchemas.MatchProperties).Ok);
            var input=Match("text_style"); input["properties"]="text_style";
            Assert.False(SchemaValidator.Validate("match_properties",input,CommandSchemas.MatchProperties).Ok);
            Assert.False(SchemaValidator.Validate("set_text_style",new JObject { ["handles"]="A",["text_style"]="GOST TYPE A" },CommandSchemas.SetTextStyle).Ok);
        }

        private static JObject Match(params string[] properties) => new JObject {
            ["source_handle"]="1",["target_handles"]=new JArray("2","3"),["properties"]=new JArray(properties) };
    }
}
