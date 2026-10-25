using System;
using System.Linq;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Annotation;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class LeaderRepositionTests
    {
        private static JObject Request() => JObject.Parse(@"{
          'expected_document':'C:\\Work\\plan.dwg','expected_fingerprint':'{A13154B0-122E-4EEB-9B56-7A592CB25EB0}',
          'changes':[{'handle':'AB','expected_layer':'НВ','expected_owner_handle':'1F','expected_leader_index':1,'expected_line_index':0,'expected_text':'{\\C150;НВ-М01}\\PH=2.40 м',
          'expected_text_location':[2,2,0],'expected_vertices':[[0,0,0],[1,2,0]],'expected_dogleg':[1,0,0],
          'new_text_location':[3,4,0],'new_terminal':[2,4,0],'dogleg_side':'left'}]}");
        [Fact]
        public void Valid_request_preserves_literal_native_contents_and_defaults_to_no_setters()
        {
            var json=Request(); Assert.True(LeaderRepositionRequest.TryParse(json,out var input,out var error),error);
            Assert.True(input.DryRun); Assert.Equal(json["changes"][0].Value<string>("expected_text"),input.Changes[0].Expected.Text);
            Assert.Equal(new[]{0d,0d,0d},input.Changes[0].Expected.Vertices[0]);
            Assert.Equal("left",input.Changes[0].DoglegSide);
        }
        [Theory]
        [InlineData("leader_index")][InlineData("line_index")][InlineData("text")][InlineData("arrow")][InlineData("terminal")][InlineData("text_location")][InlineData("dogleg")][InlineData("owner")][InlineData("layer")]
        public void Fresh_snapshot_refuses_each_stale_field(string field)
        {
            Assert.True(LeaderRepositionRequest.TryParse(Request(),out var input,out _));
            var change=input.Changes[0];var actual=new LeaderRepositionState{LeaderIndex=change.Expected.LeaderIndex,LineIndex=change.Expected.LineIndex,Layer=change.Expected.Layer,Owner=change.Expected.Owner,Text=change.Expected.Text,
                TextLocation=change.Expected.TextLocation.ToArray(),Dogleg=change.Expected.Dogleg.ToArray(),Vertices=change.Expected.Vertices.Select(p=>p.ToArray()).ToArray()};
            switch(field){case "leader_index":actual.LeaderIndex=0;break;case "line_index":actual.LineIndex=1;break;case "text":actual.Text=actual.Text.Replace("\\P","\n");break;case "arrow":actual.Vertices[0][0]=0.1;break;
                case "terminal":actual.Vertices[1][0]=9;break;case "text_location":actual.TextLocation[0]=9;break;
                case "dogleg":actual.Dogleg[0]=-1;break;case "owner":actual.Owner="2F";break;case "layer":actual.Layer="НК1";break;}
            Assert.Throws<ArgumentException>(()=>LeaderRepositionRequest.CheckSnapshot(change,actual));
        }
        [Theory]
        [InlineData("z")][InlineData("zero_segment")][InlineData("topology")][InlineData("side")][InlineData("unknown")][InlineData("coordinate")]
        public void Unsupported_targets_fail_before_any_host_call(string failure)
        {
            var json=Request();var item=(JObject)json["changes"][0];
            switch(failure){case "z":item["new_text_location"][2]=1;break;case "zero_segment":item["new_terminal"]=new JArray(0,0,0);break;
                case "topology":((JArray)item["expected_vertices"]).Add(new JArray(3,3,0));break;case "side":item["dogleg_side"]="up";break;
                case "unknown":item["new_text"]="unrequested overwrite";break;case "coordinate":item["new_text_location"][0]="3";break;}
            Assert.False(LeaderRepositionRequest.TryParse(json,out _,out var error)); Assert.False(string.IsNullOrWhiteSpace(error));
        }
        [Fact]
        public void Bounds_and_hex_aliases_are_enforced()
        {
            var json=Request();var list=(JArray)json["changes"];var duplicate=(JObject)list[0].DeepClone();duplicate["handle"]="00ab";list.Add(duplicate);
            Assert.False(LeaderRepositionRequest.TryParse(json,out _,out var error));Assert.Contains("duplicate",error);
            var example=list[0].DeepClone();list.Clear();
            for(int i=1;i<=100;i++){var item=(JObject)example.DeepClone();item["handle"]=i.ToString("X");list.Add(item);}
            Assert.True(LeaderRepositionRequest.TryParse(json,out _,out error),error);var extra=(JObject)example.DeepClone();extra["handle"]="FFF";list.Add(extra);
            Assert.False(LeaderRepositionRequest.TryParse(json,out _,out error));Assert.Contains("1..100",error);
        }
        [Fact]
        public void Actual_SDK_schema_has_required_typed_snapshot_and_no_arbitrary_text_write()
        {
            var method=typeof(LeaderTools).GetMethod(nameof(LeaderTools.RepositionLeaders));
            var tool=McpServerTool.Create(method,(object)null).ProtocolTool;
            Assert.Equal("dwg_reposition_leaders",tool.Name);Assert.False(tool.Annotations.ReadOnlyHint);Assert.False(tool.Annotations.IdempotentHint);
            var schema=tool.InputSchema;Assert.Equal(new[]{"expected_document","expected_fingerprint","changes"},schema.GetProperty("required").EnumerateArray().Select(x=>x.GetString()));
            Assert.Equal("array",schema.GetProperty("properties").GetProperty("changes").GetProperty("type").GetString());
            Assert.True(schema.GetProperty("properties").GetProperty("dry_run").GetProperty("default").GetBoolean());
            var item=JObject.Parse(System.Text.Json.JsonSerializer.Serialize(new LeaderPositionChange()));
            Assert.Equal(12,item.Properties().Count());Assert.False(item.ContainsKey("new_text"));
            Assert.True(SchemaValidator.Validate("reposition_leaders",Request(),CommandSchemas.RepositionLeaders).Ok);
        }
    }
}
