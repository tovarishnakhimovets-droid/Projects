using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Blocks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class BlockInspectionTools
    {
        [McpServerTool(Name = "dwg_inspect_block", ReadOnly = true, Idempotent = true), Description(
            "Read one explicit block reference, its effective definition, attributes with full tags, " +
            "and dynamic properties with types, units, allowed values and completeness flags. " +
            "Optional definition geometry uses definition-local drawing units, not WCS. Angular dynamic values " +
            "are raw AutoCAD radians with degree companions. Metadata is bounded; opaque evaluation graphs " +
            "are reported as undecoded, never as a complete action graph. Does not change drawing, view, selection or save. " +
            "Limits: entities 1-5000, metadata objects 0-1000, 10 seconds cooperative time and 1 MiB result. " +
            "Identity expectations are optional, but expected_document and expected_fingerprint must be supplied together.")]
        public static Task<string> InspectBlock(
            [Description("Required positive AutoCAD hexadecimal handle of one block reference.")] string handle,
            [Description("Optional full path of the expected titled DWG; supply together with expected_fingerprint.")] string expected_document = null,
            [Description("Optional nonempty expected drawing GUID; supply together with expected_document.")] string expected_fingerprint = null,
            [Description("Read direct definition entities and owned dictionary metadata; default true. Nested block definitions are not recursively opened.")] bool include_definition = true,
            [Description("Maximum direct definition entities; integer 1-5000. Also bounds attribute/property collection size; default 500.")] int max_entities = BlockInspectionInput.DefaultMaxEntities,
            [Description("Maximum owned metadata objects; integer 0-1000. Zero skips metadata; default 200.")] int max_metadata_objects = BlockInspectionInput.DefaultMaxMetadataObjects)
        {
            var request = new JObject
            {
                ["handle"] = handle, ["include_definition"] = include_definition,
                ["max_entities"] = max_entities, ["max_metadata_objects"] = max_metadata_objects
            };
            if (expected_document != null) request["expected_document"] = expected_document;
            if (expected_fingerprint != null) request["expected_fingerprint"] = expected_fingerprint;
            if (!BlockInspectionInput.TryParse(request, out var input, out var error))
                return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
            request["handle"] = input.Handle;
            if (input.ExpectedDocument != null) request["expected_document"] = input.ExpectedDocument;
            if (input.ExpectedFingerprint != null) request["expected_fingerprint"] = input.ExpectedFingerprint;
            return ToolGateway.LoggedCall("inspect_block", request, request);
        }
    }
}
