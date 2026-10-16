using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;
namespace Bimwright.Dwg.Tests
{
    public class BlockVariantTests
    {
        private static JObject Point(double x,double y,double z=0)=>new JObject{["x"]=x,["y"]=y,["z"]=z};
        private static JObject Variant()=>new JObject
        {
            ["expected_document"]=@"D:\Drawings\elbows.dwg",["expected_fingerprint"]="1e3a3e15-4e07-c947-a816-9105ed7ec14d",
            ["new_name"]="Отвод ППУ 530-710",["insertion_point"]=Point(22716,4007),
            ["expected_reference"]=new JObject{["handle"]="1D4E3",["definition_handle"]="1D4D0",["owner_handle"]="70",["effective_name"]="Отвод ППУ ду400",["layer"]="0",["position"]=Point(25216,4007),["normal"]=Point(0,0,1),["scale"]=Point(1,1,1),["rotation_radians"]=0,["dynamic_values"]=new JObject{["Угол1"]=Math.PI,["Угол2"]=Math.PI/2},["attributes"]=new JArray()},
            ["polyline_edits"]=new JArray(new JObject{["handle"]="1E3ED",["expected_layer"]="0",["expected_vertices"]=new JArray(Vertex(-1100,0),Vertex(0,0)),["new_vertices"]=new JArray(Point(-1200,0),Point(0,0))}),
            ["attribute_edits"]=new JArray(),["mtext_edits"]=new JArray(),["attribute_values"]=new JObject(),["dry_run"]=true
        };
        private static JObject Vertex(double x,double y)=>new JObject{["point"]=Point(x,y),["bulge"]=0,["start_width"]=0,["end_width"]=0};
        private static JObject Angles()=>new JObject
        {
            ["expected_document"]=@"D:\Drawings\elbows.dwg",["expected_fingerprint"]="1e3a3e15-4e07-c947-a816-9105ed7ec14d",
            ["handle"]="123",["expected_name"]="Отвод ППУ 530-710",["expected_angles"]=new JObject{["Угол1"]=Math.PI,["Угол2"]=Math.PI/2},["new_angles"]=new JObject(),
            ["binding"]=new JObject{["tag"]="МАССА_ЕД._КГ",["expected_text"]="90",["primary_property"]="Угол1",["secondary_property"]="Угол2",["mode"]="deflection_between_arms",["precision"]=0}
        };
        [Fact] public void Valid_plan_retains_exact_local_and_WCS_geometry_without_writing()
        {var request=Variant();Assert.True(BlockVariantInput.TryParse(request,out var input,out var error),error);Assert.True(input.DryRun);Assert.Equal(-1200,(double)input.PolylineEdits[0]["new_vertices"][0]["x"]);Assert.Equal(22716,(double)input.InsertionPoint["x"]);Assert.True(SchemaValidator.Validate("create_block_variant",request,CommandSchemas.CreateBlockVariant).Ok);}
        [Theory]
        [InlineData("identity")][InlineData("fingerprint")][InlineData("anonymous")][InlineData("duplicate")][InlineData("coincident")][InlineData("width")][InlineData("scope")][InlineData("nan")][InlineData("tag")]
        public void Unsafe_or_stale_scope_inputs_are_rejected_before_native_access(string kind)
        {
            var o=Variant();
            switch(kind)
            {
                case "identity":o["expected_document"]="elbows.dwg";break;
                case "fingerprint":o["expected_fingerprint"]=Guid.Empty.ToString();break;
                case "anonymous":o["new_name"]="*U13";break;
                case "duplicate":((JArray)o["polyline_edits"]).Add(o["polyline_edits"][0].DeepClone());break;
                case "coincident":o["polyline_edits"][0]["new_vertices"][0]=o["polyline_edits"][0]["new_vertices"][1].DeepClone();break;
                case "width":o["polyline_edits"][0]["expected_vertices"][0]["start_width"]=3;break;
                case "scope":o["erase_source"]=true;break;
                case "nan":o["insertion_point"]["x"]=double.NaN;break;
                case "tag":o["attribute_values"]["nonexistent"]="95";break;
            }
            Assert.False(BlockVariantInput.TryParse(o,out _,out var error));Assert.False(string.IsNullOrWhiteSpace(error));
        }
        [Theory]
        [InlineData(180,90,90)][InlineData(180,45,45)][InlineData(225,90,45)][InlineData(270,90,0)][InlineData(90,90,180)][InlineData(540,405,45)][InlineData(-135,90,45)]
        public void Bend_deflection_uses_two_arm_directions_and_wraps_full_rotations(double a,double b,int expected)
        {Assert.Equal(expected.ToString(),BlockAngleInput.Format(a*Math.PI/180,b*Math.PI/180,"deflection_between_arms",0));}
        [Fact] public void Decimal_format_is_invariant_and_does_not_emit_negative_zero()
        {Assert.Equal("12.35",BlockAngleInput.Format(12.345*Math.PI/180,0,"property_degrees",2));Assert.Equal("0",BlockAngleInput.Format(-1e-14,0,"property_degrees",2));}
        [Fact] public void Bind_requires_existing_named_properties_and_exact_tag_text()
        {var o=Angles();Assert.True(BlockAngleInput.TryParse(o,out _,out var error),error);o["binding"]["primary_property"]="Parameter(2)";Assert.False(BlockAngleInput.TryParse(o,out _,out _));}
        [Fact] public void Setter_cannot_broaden_property_scope_or_accept_degree_strings()
        {var o=Angles();o["new_angles"]["Other"]=90;Assert.False(BlockAngleInput.TryParse(o,out _,out _));o=Angles();o["new_angles"]["Угол2"]="45°";Assert.False(BlockAngleInput.TryParse(o,out _,out _));}
        [Fact] public void Actual_MCP_schema_has_typed_nested_snapshots_and_write_annotations()
        {
            var variant=McpServerTool.Create(typeof(BlockVariantTools).GetMethod(nameof(BlockVariantTools.CreateBlockVariant)),(object)null).ProtocolTool;
            Assert.False(variant.Annotations.ReadOnlyHint);Assert.False(variant.Annotations.IdempotentHint);
            var properties=variant.InputSchema.GetProperty("properties");
            Assert.Equal("object",properties.GetProperty("expected_reference").GetProperty("type").GetString());
            Assert.Contains("attributes",properties.GetProperty("expected_reference").GetProperty("properties").EnumerateObject().Select(p=>p.Name));
            Assert.Equal("array",properties.GetProperty("polyline_edits").GetProperty("type").GetString());
            var angles=McpServerTool.Create(typeof(BlockVariantTools).GetMethod(nameof(BlockVariantTools.UpdateBlockAngles)),(object)null).ProtocolTool;
            Assert.False(angles.Annotations.ReadOnlyHint);Assert.Contains("expected_angles",angles.InputSchema.GetProperty("required").EnumerateArray().Select(p=>p.GetString()));
        }
        [Fact] public void Read_only_registration_never_exposes_block_writes()
        {
            var method=typeof(Bimwright.Dwg.Server.Program).GetMethod("ResolveToolTypesForRegistration",BindingFlags.NonPublic|BindingFlags.Static);
            var types=(IEnumerable<Type>)method.Invoke(null,new object[]{new HashSet<string>(new[]{"modify","query"},StringComparer.OrdinalIgnoreCase),true});Assert.DoesNotContain(typeof(BlockVariantTools),types);
        }
        [Fact] public async Task Invalid_facade_request_returns_locally_without_a_native_call()
        {var response=JObject.Parse(await BlockVariantTools.UpdateBlockAngles("relative.dwg",Guid.NewGuid().ToString(),"123","name",new Dictionary<string,double>{{"Угол2",Math.PI/2}},new Dictionary<string,double>{{"Угол2",Math.PI/4}}));Assert.False(response.Value<bool>("ok"));}
    }
}
