using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Hatches;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public sealed class HatchClipQueryTools
    {
        [McpServerTool(Name = "dwg_plan_hatch_clip", ReadOnly = true, Idempotent = true), Description(
            "Plan clipping of 1-20 explicit direct model-space HATCH handles to the positive UNION of 1-32 closed lightweight polyline masks. Active model tab required. " +
            "Uses native Region booleans on unopened transient line/arc curves; no tessellation. Masks and hatches must be +Z/elevation0; hatches nonassociative, Normal island style, nongradient. " +
            "Holes and separate islands are preserved. Refuses unsupported curves, metadata/associations, locked/dependent layers and nested block contents. " +
            "Returns keep/remove/clip decisions, areas, planned output counts, drawing session and geometry plan hash. Never moves masks or writes/saves/selects/changes view. " +
            "Limits: 1000 source loops, 20000 aggregate inspected/output edges, 2000 output hatches, 15-second cooperative deadline; a native modeler call cannot be interrupted.")]
        public static Task<string> PlanHatchClip(string expected_document, string expected_fingerprint, string[] hatch_handles, string[] boundary_handles)
            => HatchClipWriteTools.Call(new { expected_document, expected_fingerprint, hatch_handles, boundary_handles }, true);
    }
    [McpServerToolType]
    public sealed class HatchClipWriteTools
    {
        [McpServerTool(Name = "dwg_clip_hatches", ReadOnly = false, Idempotent = false), Description(
            "Apply dwg_plan_hatch_clip to the same explicit HATCH/mask handles with its expected_session and expected_plan_hash. dry_run defaults true and performs native transient planning only. " +
            "Real write recomputes and compares source/mask geometry under the document write lock, then commits the selected batch in one transaction. " +
            "Keeps inside hatch handles, erases outside hatches, replaces crossings with cloned hatch appearance and exact BREP loops; holes retained. Temporary construction curves are erased in the transaction. " +
            "Checks retained area and geometry before erasing originals; returns source-to-created handles. Same bounds/refusals as planner. No save; masks unchanged. " +
            "Single Undo requires live verification. Never repeat a timed-out mutation with uncertain outcome.")]
        public static Task<string> ClipHatches(string expected_document, string expected_fingerprint, string[] hatch_handles, string[] boundary_handles,
            string expected_session, string expected_plan_hash, bool dry_run = true)
            => Call(new { expected_document, expected_fingerprint, hatch_handles, boundary_handles, expected_session, expected_plan_hash, dry_run }, false);
        internal static Task<string> Call(object value, bool plan)
        {
            var request = JObject.Parse(System.Text.Json.JsonSerializer.Serialize(value));
            if (!HatchClipInput.TryParse(request, plan, out _, out var error))
                return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
            return ToolGateway.LoggedCall(plan ? "plan_hatch_clip" : "clip_hatches", request, request);
        }
    }
}
