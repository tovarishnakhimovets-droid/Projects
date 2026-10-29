using System;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Blocks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;

namespace Bimwright.Dwg.Server.Tools
{
    public sealed class BlockExplodeSnapshot
    {
        [JsonRequired] public string handle { get; set; }
        [JsonRequired] public string owner_handle { get; set; }
        [JsonRequired] public string definition_handle { get; set; }
        [JsonRequired] public string definition_name { get; set; }
        [JsonRequired] public string layer { get; set; }
        [JsonRequired] public BlockPoint position { get; set; }
        [JsonRequired] public BlockPoint normal { get; set; }
        [JsonRequired] public BlockPoint scale { get; set; }
        [JsonRequired] public double rotation_radians { get; set; }
        [JsonRequired] public int attribute_count { get; set; }
        [JsonRequired] public int definition_entity_count { get; set; }
    }

    [McpServerToolType]
    public sealed class BlockExplodeQueryTools
    {
        [McpServerTool(Name = "dwg_plan_block_explode", ReadOnly = true, Idempotent = true), Description(
            "Inspect 1-100 explicit ordinary model-space block references for one-level explode. Returns exact reference snapshots and a drawing-session revision guard; counts direct definition entities without constructing exploded geometry. " +
            "Refuses xrefs/dependent, dynamic/annotative, attributes, extension dictionaries/clipping, XData/reactors, nonexplodable or locked targets and nonpositive/nonuniform scale. " +
            "max_entities bounds aggregate direct definition inspection, 1-20000. 15-second cooperative budget. No entity construction, write, selection, view change or save. Use fresh plan after any drawing edit.")]
        public static Task<string> PlanBlockExplode(string expected_document, string expected_fingerprint, string[] handles, int max_entities = 20000)
            => BlockExplodeWriteTools.Call(new { expected_document, expected_fingerprint, handles, max_entities }, true);

    }
    [McpServerToolType]
    public sealed class BlockExplodeWriteTools
    {
        [McpServerTool(Name = "dwg_explode_blocks", ReadOnly = false, Idempotent = false), Description(
            "Explode 1-100 explicitly guarded ordinary model-space BlockReferences by ONE level in one transaction. Hatches stay hatches; nested blocks remain blocks for a later explicit plan. " +
            "Requires full DWG path/fingerprint, the unchanged drawing session/revision and exact snapshots from dwg_plan_block_explode. Same refusals as the planner. " +
            "dry_run defaults true and never calls Explode or opens objects for write. max_entities 1-20000 bounds inspection and actual output; 15-second cooperative deadline cannot interrupt a native Explode call. " +
            "Erases originals only after exploded entities are appended and transaction-owned. No automatic recursive cleanup, purge, save, view or selection change. Single-Undo behavior requires live verification. Never repeat an uncertain timed-out mutation.")]
        public static Task<string> ExplodeBlocks(string expected_document, string expected_fingerprint, string expected_session,
            long expected_revision, BlockExplodeSnapshot[] expected_snapshots, bool dry_run = true, int max_entities = 20000)
            => Call(new { expected_document, expected_fingerprint, expected_session, expected_revision, expected_snapshots, dry_run, max_entities }, false);

        internal static Task<string> Call(object value, bool plan)
        {
            try
            {
                var request = JObject.Parse(System.Text.Json.JsonSerializer.Serialize(value));
                if (!BlockExplodeInput.TryParse(request, plan, out _, out var error))
                    return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
                return ToolGateway.LoggedCall(plan ? "plan_block_explode" : "explode_blocks", request, request);
            }
            catch (Exception e) when (e is ArgumentException || e is System.Text.Json.JsonException)
            { return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error = e.Message })); }
        }
    }
}
