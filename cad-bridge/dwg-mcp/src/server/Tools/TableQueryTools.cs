using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public sealed class TableQueryTools
    {
        [McpServerTool(Name = "dwg_read_table", ReadOnly = true, Idempotent = true), Description(
            "Read one native TABLE in the active drawing by handle, guarded by the exact saved path/fingerprint. " +
            "Returns all plain cell texts, row heights, column widths, merges, formatting and bounds. " +
            "At most 10000 cells/200000 text characters. No updates, field evaluation, layout regeneration or save. " +
            "Inactive targets are refused; this reader preserves Undo/Redo through a read lock and OpenCloseTransaction.")]
        public static Task<string> ReadTable(string expected_document, string expected_fingerprint, string handle)
        {
            var request = new { expected_document, expected_fingerprint, handle };
            return ToolGateway.LoggedCall("read_table", request, request);
        }
    }
}
