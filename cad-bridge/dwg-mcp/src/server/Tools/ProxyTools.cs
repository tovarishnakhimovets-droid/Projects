using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Proxy;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public sealed class ProxyTools
    {
        [McpServerTool(Name = "dwg_inventory_proxies", ReadOnly = true, Idempotent = true), Description(
            "Read proxies throughout the active database, including block definitions and nongraphical objects. " +
            "A bounded native handle scan reports original classes, applications, owners and native erase permission. " +
            "complete is true only for a scan from handle 1 that reaches the captured handseed without errors or limits. " +
            "range_complete covers only this page's suffix. Resume with next_after_handle and matching expected document/fingerprint. " +
            "Pages are independent reads; check their handseed. Classification is a metadata text hint, never erase authorization.")]
        public static Task<string> InventoryProxies(
            [Description("Only full_database is supported.")] string scope = "full_database",
            [Description("Optional continuation cursor from last_handle_scanned; requires both expected identity fields.")] string after_handle = null,
            [Description("Maximum handle numbers attempted, including unused gaps: 1-1000000.")] int max_handles = 500000,
            [Description("Read time budget in milliseconds, 100-20000; native calls finish synchronously.")] int budget_ms = 5000,
            [Description("Maximum proxy records returned, 1-5000; serialized response is also capped at 2 MiB.")] int max_results = 1000,
            [Description("Maximum owner chain depth per proxy, 1-32.")] int owner_depth = 8,
            [Description("Optional full DWG path guard; mandatory for continuation.")] string expected_document = null,
            [Description("Optional fingerprint GUID paired with expected_document; mandatory for continuation.")] string expected_fingerprint = null)
        {
            var request = new JObject { ["scope"] = scope, ["max_handles"] = max_handles,
                ["budget_ms"] = budget_ms, ["max_results"] = max_results, ["owner_depth"] = owner_depth };
            if (after_handle != null) request["after_handle"] = after_handle;
            if (expected_document != null) request["expected_document"] = expected_document;
            if (expected_fingerprint != null) request["expected_fingerprint"] = expected_fingerprint;
            if (!ProxyInventoryInput.TryParse(request, out _, out var error)) return ProxyInputError.Return(error);
            return ToolGateway.LoggedCall("inventory_proxies", request, request);
        }
    }

    [McpServerToolType]
    public sealed class ProxyWriteTools
    {
        [McpServerTool(Name = "dwg_erase_proxy_objects", ReadOnly = false, Idempotent = false), Description(
            "Erase 1-1000 explicitly identified nongraphical ProxyObjects in one guarded transaction. " +
            "Requires the current full DWG path, fingerprint and exact original class/DXF/application/owner for each handle. " +
            "Refuses native objects, ProxyEntities, missing erase permission, custom-data references, nonempty extension dictionaries, " +
            "unavailable or xref owners and locked owning entity layers. No implicit dictionary/service roots are selected. " +
            "Any preflight or erase failure aborts the entire request. dry_run validates without write access. " +
            "Native reactors may affect associated state. Does not purge, save, or promise grouped Undo; never retry an uncertain mutation.")]
        public static Task<string> EraseProxyObjects(
            [Description("Exact full absolute DWG path from the current inventory.")] string expected_document,
            [Description("Fingerprint GUID from the current inventory.")] string expected_fingerprint,
            [Description("1-1000 unique explicit proxy expectations copied from inspected records.")] ProxyEraseTarget[] targets,
            [Description("Validate only; no write access, erase or commit.")] bool dry_run = false)
        {
            var request = new JObject { ["expected_document"] = expected_document, ["expected_fingerprint"] = expected_fingerprint,
                ["targets"] = targets == null ? JValue.CreateNull() : JArray.FromObject(targets), ["dry_run"] = dry_run };
            if (!ProxyEraseInput.TryParse(request, out _, out var error)) return ProxyInputError.Return(error);
            return ToolGateway.LoggedCall("erase_proxy_objects", request, request);
        }
    }

    public sealed class ProxyEraseTarget
    {
        [System.Text.Json.Serialization.JsonRequired, Description("Explicit nongraphical proxy handle, as a positive hexadecimal string.")]
        public string handle { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Exact original_class reported by inventory.")]
        public string expected_original_class { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Exact original_dxf, including an empty string if reported.")]
        public string expected_original_dxf { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Exact application description, including an empty string if reported.")]
        public string expected_application { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Exact owner_handle reported by inventory.")]
        public string expected_owner_handle { get; set; }
    }

    internal static class ProxyInputError
    {
        internal static Task<string> Return(string error)
            => Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
    }
}
