using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Blocks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonRequired=System.Text.Json.Serialization.JsonRequiredAttribute;
using JsonIgnore=System.Text.Json.Serialization.JsonIgnoreAttribute;
namespace Bimwright.Dwg.Server.Tools
{
    public sealed class BlockPoint { [JsonRequired] public double x{get;set;} [JsonRequired] public double y{get;set;} [JsonRequired] public double z{get;set;} }
    public sealed class BlockAttributeSnapshot
    {
        [JsonRequired] public string handle{get;set;} [JsonRequired] public string tag{get;set;} [JsonRequired] public string text{get;set;}
        [JsonRequired] public BlockPoint position{get;set;} [JsonRequired] public BlockPoint alignment_point{get;set;}
        [JsonRequired] public double rotation_radians{get;set;} [JsonRequired] public double height{get;set;}
        [JsonRequired] public bool invisible{get;set;} [JsonRequired] public bool constant{get;set;} [JsonRequired] public bool preset{get;set;}
        [JsonRequired] public bool verifiable{get;set;} [JsonRequired] public bool lock_position_in_block{get;set;}
        [JsonRequired] public bool is_mtext_attribute{get;set;} [JsonRequired] public bool fields_present{get;set;}
    }
    public sealed class BlockReferenceSnapshot
    {
        [JsonRequired] public string handle{get;set;} [JsonRequired] public string effective_name{get;set;} [JsonRequired] public string definition_handle{get;set;}
        [JsonRequired] public string owner_handle{get;set;} [JsonRequired] public string layer{get;set;}
        [JsonRequired] public BlockPoint position{get;set;} [JsonRequired] public BlockPoint normal{get;set;} [JsonRequired] public BlockPoint scale{get;set;}
        [JsonRequired] public double rotation_radians{get;set;}
        [JsonRequired,Description("Complete named writable angular properties in raw radians.")] public Dictionary<string,double> dynamic_values{get;set;}
        [JsonRequired] public BlockAttributeSnapshot[] attributes{get;set;}
    }
    public sealed class BlockVertexSnapshot
    { [JsonRequired] public BlockPoint point{get;set;} [JsonRequired] public double bulge{get;set;} [JsonRequired] public double start_width{get;set;} [JsonRequired] public double end_width{get;set;} }
    public sealed class BlockPolylineEdit
    {
        [JsonRequired] public string handle{get;set;} [JsonRequired] public string expected_layer{get;set;}
        [JsonRequired] public BlockVertexSnapshot[] expected_vertices{get;set;}
        [JsonRequired,Description("Two new definition-local points at the same elevation.")] public BlockPoint[] new_vertices{get;set;}
    }
    public sealed class BlockAttributeDefaultEdit
    {
        [JsonRequired] public string handle{get;set;} [JsonRequired] public string expected_layer{get;set;} [JsonRequired] public string tag{get;set;}
        [JsonRequired] public string expected_default{get;set;} [JsonRequired] public string new_default{get;set;}
    }
    public sealed class BlockMTextEdit
    {
        [JsonRequired] public string handle{get;set;} [JsonRequired] public string expected_layer{get;set;}
        [JsonRequired] public string expected_text{get;set;} [JsonRequired] public string new_text{get;set;} [JsonRequired] public BlockPoint expected_location{get;set;}
    }
    public class BlockAngleBindingSpec
    {
        [JsonRequired] public string tag{get;set;}
        [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public string primary_property{get;set;}
        [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public string secondary_property{get;set;}
        [JsonRequired,Description("dimension_display_degrees reads the unique angular dimension of the CURRENT evaluated reference BTR and follows its native decimal formatting; do not supply property names or precision. Legacy property_degrees/deflection_between_arms use named raw angular properties.")] public string mode{get;set;}
        [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull),Description("Required0..6 only for legacy property formulas. Omit for native dimension display formatting.")] public int? precision{get;set;}
    }
    public sealed class BlockAngleBinding:BlockAngleBindingSpec
    {
        [JsonRequired] public string expected_text{get;set;}
    }
    [McpServerToolType]
    public sealed class BlockVariantTools
    {
        [McpServerTool(Name="dwg_create_block_variant",ReadOnly=false,Idempotent=false),Description(
            "Create one independent named dynamic variant from a guarded current-space reference. Native whole-BTR clone preserves owned graph; edits1..32 open straight2-vertex zero-width polylines,0..32 single-line attribute defaults and0..8 MTexts. " +
            "Insert at insertion_point and clone single-line attached attributes retaining presentation/flags with explicit overrides. Definition geometry is LOCAL; source/insertion are WCS drawing units. Source normal+Z and unit scale only. " +
            "Checks full titled DWG path/fingerprint, source identity, complete writable-angle/attribute snapshots and edited entity snapshots. Refuses source fields/multiline attributes, external definitions, collisions and locked source layer. " +
            "dry_run validates without writing. Native clone APIs may use internal transactions; failure cleanup is explicit best effort. Verify new grips/action behavior.15second cooperative budget. No save/view/selection changes. Never repeat uncertain timeout.")]
        public static Task<string> CreateBlockVariant(string expected_document,string expected_fingerprint,string new_name,BlockPoint insertion_point,
            BlockReferenceSnapshot expected_reference,BlockPolylineEdit[] polyline_edits,BlockAttributeDefaultEdit[] attribute_edits,
            BlockMTextEdit[] mtext_edits,Dictionary<string,string> attribute_values,bool dry_run=false,double geometry_tolerance=1e-8)
        {
            try
            {
                var request=Wire(new{expected_document,expected_fingerprint,new_name,insertion_point,expected_reference,polyline_edits,attribute_edits,mtext_edits,attribute_values,dry_run,geometry_tolerance});
                if(!BlockVariantInput.TryParse(request,out _,out var error))return Error(error);
                return ToolGateway.LoggedCall("create_block_variant",request,request);
            }
            catch(Exception e)when(e is ArgumentException||e is System.Text.Json.JsonException){return Error(e.Message);}
        }
        [McpServerTool(Name="dwg_update_block_angles",ReadOnly=false,Idempotent=false),Description(
            "Set explicitly named writable Angular properties on one guarded current-space dynamic reference; ALL values are RAW RADIANS. expected_angles is the complete named writable-angle snapshot. " +
            "new_angles may be empty only to create a binding. Optional binding persists on the guarded definition and applies to all its references. Automatic post-command/grip updates REQUIRE this plugin. " +
            "This is a plugin calculation, not a native portable field. Derived attribute writes do not disable database Undo; grip/Undo/Redo behavior needs native live verification. " +
            "dimension_display_degrees copies native formatted numeric text from exactly one direct evaluated angular dimension; decimal degrees only, no literal text overrides, tolerances or limits. Re-resolves current BTR on every refresh. " +
            "Replacing an existing binding requires its exact expected_binding snapshot and retains its attribute tag. Rejects fields, ambiguous dimensions/tags/properties, locked layers and changed document/name/angle/text/binding snapshots. dry_run uses no DB setters. No save. Never repeat uncertain timeout.")]
        public static Task<string> UpdateBlockAngles(string expected_document,string expected_fingerprint,string handle,string expected_name,
            Dictionary<string,double> expected_angles,Dictionary<string,double> new_angles,BlockAngleBinding binding=null,bool dry_run=false,BlockAngleBindingSpec expected_binding=null)
        {
            try
            {
                var request=Wire(new{expected_document,expected_fingerprint,handle,expected_name,expected_angles,new_angles,binding,expected_binding,dry_run});
                if(!BlockAngleInput.TryParse(request,out _,out var error))return Error(error);
                return ToolGateway.LoggedCall("update_block_angles",request,request);
            }
            catch(Exception e)when(e is ArgumentException||e is System.Text.Json.JsonException){return Error(e.Message);}
        }
        private static JObject Wire(object value)=>JObject.Parse(System.Text.Json.JsonSerializer.Serialize(value));
        private static Task<string> Error(string message)=>Task.FromResult(JsonConvert.SerializeObject(new{ok=false,error=message}));
    }
}
