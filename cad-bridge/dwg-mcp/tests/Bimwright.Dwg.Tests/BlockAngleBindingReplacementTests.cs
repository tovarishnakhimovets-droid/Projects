using System;
using System.Linq;
using System.Text.Json;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;
namespace Bimwright.Dwg.Tests
{
    public class BlockAngleBindingReplacementTests
    {
        private static JObject Request()=>new JObject
        {
            ["expected_document"]=@"D:\Drawings\elbows.dwg",["expected_fingerprint"]="1e3a3e15-4e07-c947-a816-9105ed7ec14d",
            ["handle"]="20F76",["expected_name"]="Отвод ППУ 530-710",
            ["expected_angles"]=new JObject{["Угол1"]=Math.PI,["Угол2"]=1.901048356063919},["new_angles"]=new JObject(),
            ["binding"]=new JObject{["tag"]="МАССА_ЕД._КГ",["expected_text"]="109",["mode"]="dimension_display_degrees"},
            ["expected_binding"]=new JObject{["tag"]="МАССА_ЕД._КГ",["primary_property"]="Угол2",["mode"]="property_degrees",["precision"]=0},["dry_run"]=true
        };
        [Fact] public void Existing_formula_can_be_replaced_by_native_dimension_display_without_changing_angles_or_tag()
        {var request=Request();Assert.True(BlockAngleInput.TryParse(request,out var input,out var error),error);Assert.True(input.DryRun);Assert.Empty((JObject)input.Request["new_angles"]);Assert.True(SchemaValidator.Validate("update_block_angles",request,CommandSchemas.UpdateBlockAngles).Ok);}
        [Theory]
        [InlineData("tag")][InlineData("precision")][InlineData("raw_property")][InlineData("missing_text")][InlineData("without_replacement")][InlineData("stale_property")][InlineData("unknown_old_key")][InlineData("renamed_mode")]
        public void Replacement_contract_rejects_broadened_or_ambiguous_requests(string scenario)
        {
            var o=Request();
            switch(scenario)
            {
                case "tag":o["binding"]["tag"]="OTHER";break;
                case "precision":o["binding"]["precision"]=0;break;
                case "raw_property":o["binding"]["primary_property"]="Угол2";break;
                case "missing_text":((JObject)o["binding"]).Remove("expected_text");break;
                case "without_replacement":((JObject)o).Remove("binding");break;
                case "stale_property":o["expected_binding"]["primary_property"]="missing";break;
                case "unknown_old_key":o["expected_binding"]["ignore_mismatch"]=true;break;
                case "renamed_mode":o["binding"]["mode"]="best_guess";break;
            }
            Assert.False(BlockAngleInput.TryParse(o,out _,out var error));Assert.False(string.IsNullOrWhiteSpace(error));
        }
        [Fact] public void Native_display_request_does_not_require_a_fabricated_property_or_precision()
        {var o=Request();((JObject)o).Remove("expected_binding");Assert.True(BlockAngleInput.TryParse(o,out _,out var error),error);}
        [Fact] public void Actual_SDK_schema_exposes_optional_typed_expected_binding_and_native_display_mode()
        {
            var tool=McpServerTool.Create(typeof(BlockVariantTools).GetMethod(nameof(BlockVariantTools.UpdateBlockAngles)),(object)null).ProtocolTool;
            var p=tool.InputSchema.GetProperty("properties");Assert.True(p.TryGetProperty("expected_binding",out var b));
            Assert.Contains("mode",b.GetProperty("properties").EnumerateObject().Select(v=>v.Name));
            Assert.DoesNotContain("expected_text",b.GetProperty("properties").EnumerateObject().Select(v=>v.Name));
            Assert.DoesNotContain("expected_binding",tool.InputSchema.GetProperty("required").EnumerateArray().Select(v=>v.GetString()));
            Assert.False(tool.Annotations.ReadOnlyHint);
        }
        [Fact] public void DTO_preserves_exact_old_record_and_omits_legacy_fields_in_native_spec()
        {
            var binding=new BlockAngleBinding{tag="МАССА_ЕД._КГ",expected_text="109",mode="dimension_display_degrees"};
            var old=new BlockAngleBindingSpec{tag="МАССА_ЕД._КГ",mode="property_degrees",primary_property="Угол2",precision=0};
            var b=JObject.Parse(System.Text.Json.JsonSerializer.Serialize(binding));Assert.Null(b["precision"]);Assert.Null(b["primary_property"]);Assert.Null(b["secondary_property"]);
            var previous=JObject.Parse(System.Text.Json.JsonSerializer.Serialize(old));Assert.True(JToken.DeepEquals(Request()["expected_binding"],previous));
        }
    }
}
