using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;
using static Bimwright.Dwg.Tests.McpSchemaAssertions;

namespace Bimwright.Dwg.Tests
{
    public sealed class McpStdioProfileTests
    {
        // Exercise the built entry point and real SDK registration/stdio handshake.
        // Only initialize and tools/list are sent: these tests never call a CAD tool.
        [Theory]
        [InlineData("query,modify,drawing,view", false, 65)]
        [InlineData("query,modify,drawing,view,annotation", false, 66)]
        [InlineData("query,modify,drawing,view", true, 26)]
        [InlineData("query", false, 19)]
        [InlineData("modify", false, 32)]
        [InlineData("drawing", false, 8)]
        [InlineData("annotation", false, 4)]
        [InlineData("drawing,annotation", false, 9)]
        public async Task Built_server_initializes_and_advertises_the_exact_profile(
            string profile, bool readOnly, int expectedCount)
        {
            string server = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "src", "server", "bin", "Release", "net8.0",
                "Bimwright.Dwg.Server.dll"));
            Assert.True(File.Exists(server), "Build the Release server before running the stdio contract test: " + server);
            string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (string.IsNullOrWhiteSpace(dotnet)) dotnet = "dotnet";

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "dwg-offline-profile-" + profile,
                Command = dotnet,
                Arguments = new[] { server, "--target", "2026", "--toolsets", profile, "--disable-toolbaker",
                    // A unique missing path isolates this test from the installed user's config.
                    "--config", Path.Combine(Path.GetDirectoryName(server), "offline-profile-" + Guid.NewGuid().ToString("N") + ".json") },
                WorkingDirectory = Path.GetDirectoryName(server),
                ShutdownTimeout = TimeSpan.FromSeconds(2),
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["BIMWRIGHT_DWG_TARGET"] = "2026",
                    ["BIMWRIGHT_DWG_TOOLSETS"] = profile,
                    ["BIMWRIGHT_DWG_ENABLE_TOOLBAKER"] = "false",
                    ["BIMWRIGHT_DWG_READ_ONLY"] = readOnly ? "true" : "false"
                }
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var client = await McpClient.CreateAsync(transport,
                new McpClientOptions
                {
                    ClientInfo = new Implementation { Name = "dwg-offline-profile-tests", Version = "1.0" },
                    InitializationTimeout = TimeSpan.FromSeconds(15)
                }, cancellationToken: timeout.Token);

            Assert.Equal("dwg-mcp", client.ServerInfo.Name);
            Assert.NotNull(client.ServerCapabilities.Tools);
            var tools = await client.ListToolsAsync(options: null, cancellationToken: timeout.Token);
            var names = tools.Select(tool => tool.ProtocolTool.Name).ToArray();
            Assert.Equal(expectedCount, names.Length);
            Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
            Assert.All(names, name => Assert.StartsWith("dwg_", name, StringComparison.Ordinal));
            Assert.DoesNotContain("dwg_list_baked_tools", names);
            Assert.DoesNotContain("dwg_translate_and_rewrite", names);
            Assert.DoesNotContain("dwg_apply_unicode_style", names);

            var enabled = profile.Split(',').ToHashSet(StringComparer.Ordinal);
            Assert.Equal(enabled.Contains("query"), names.Contains("dwg_get_crosshair_settings", StringComparer.Ordinal));
            Assert.Equal(!readOnly && enabled.Contains("modify"), names.Contains("dwg_set_crosshair_settings", StringComparer.Ordinal));
            Assert.Equal(enabled.Contains("query"), names.Contains("dwg_list_settings", StringComparer.Ordinal));
            Assert.Equal(enabled.Contains("query"), names.Contains("dwg_get_settings", StringComparer.Ordinal));
            Assert.Equal(!readOnly && enabled.Contains("modify"), names.Contains("dwg_set_settings", StringComparer.Ordinal));
            bool hasLisp = !readOnly && (enabled.Contains("modify") || enabled.Contains("meta") || enabled.Contains("code"));
            Assert.Equal(hasLisp, names.Contains("dwg_run_lisp", StringComparer.Ordinal));
            Assert.Equal(enabled.Contains("query"), names.Contains("dwg_get_lisp_status", StringComparer.Ordinal));
            Assert.Equal(enabled.Contains("query"), names.Contains("dwg_get_selection", StringComparer.Ordinal));
            Assert.Equal(enabled.Contains("query"), names.Contains("dwg_list_documents", StringComparer.Ordinal));
            Assert.Equal(enabled.Contains("query"), names.Contains("dwg_use_document", StringComparer.Ordinal));
            if (enabled.Contains("query"))
            {
                var selection = Assert.Single(tools, value => value.ProtocolTool.Name == "dwg_get_selection").ProtocolTool;
                Assert.True(selection.Annotations.ReadOnlyHint);
                Assert.True(selection.Annotations.IdempotentHint);
                AssertRequired(selection.InputSchema);
                Assert.Equal(new[] { "include_geometry", "max_entities" }, selection.InputSchema.GetProperty("properties")
                    .EnumerateObject().Select(value => value.Name).OrderBy(value => value, StringComparer.Ordinal).ToArray());
            }
            if (hasLisp)
            {
                var lisp = Assert.Single(tools, tool => tool.ProtocolTool.Name == "dwg_run_lisp").ProtocolTool;
                Assert.False(lisp.Annotations.ReadOnlyHint);
                Assert.False(lisp.Annotations.IdempotentHint);
                AssertRequired(lisp.InputSchema, "file", "command", "expected_sha256", "expected_document", "expected_fingerprint");
            }
            bool hasTextCreation = !readOnly && (enabled.Contains("drawing") || enabled.Contains("annotation"));
            Assert.Equal(hasTextCreation ? 1 : 0, names.Count(name => name == "dwg_create_mtext"));
            Assert.Equal(hasTextCreation ? 1 : 0, names.Count(name => name == "dwg_create_text"));
            Assert.Equal(hasTextCreation ? 1 : 0, names.Count(name => name == "dwg_create_leader"));

            if (!readOnly && enabled.Contains("drawing"))
            {
                var batch = Assert.Single(tools, tool => tool.ProtocolTool.Name == "dwg_create_entities").ProtocolTool;
                Assert.False(batch.Annotations.ReadOnlyHint);
                Assert.False(batch.Annotations.IdempotentHint);
                var item = TypedArrayItem(batch.InputSchema, batch.InputSchema, "entities");
                AssertRequired(item, "id", "type", "layer");
                foreach (string name in new[] { "id", "type", "layer", "start", "center", "position", "points", "boundary_id" })
                    Assert.True(item.GetProperty("properties").TryGetProperty(name, out _), "Missing typed entity field: " + name);
            }
            else Assert.DoesNotContain("dwg_create_entities", names);

            if (!readOnly && enabled.Contains("modify"))
            {
                foreach (var item in new[] { ("dwg_update_texts", "items"), ("dwg_collapse_and_rewrite", "clusters") })
                {
                    var tool = Assert.Single(tools, value => value.ProtocolTool.Name == item.Item1).ProtocolTool;
                    Assert.False(tool.Annotations.ReadOnlyHint);
                    Assert.Equal(item.Item1 == "dwg_update_texts", tool.Annotations.IdempotentHint);
                    AssertRequired(tool.InputSchema, item.Item2);
                    var properties = tool.InputSchema.GetProperty("properties");
                    Assert.Equal(new[] { item.Item2 }, properties.EnumerateObject().Select(value => value.Name).ToArray());
                    AssertSchemaType(properties.GetProperty(item.Item2), "string");
                }
                var edit = Assert.Single(tools, tool => tool.ProtocolTool.Name == "dwg_edit_polyline_vertices").ProtocolTool;
                Assert.False(edit.Annotations.ReadOnlyHint);
                Assert.False(edit.Annotations.IdempotentHint);
                var change = TypedArrayItem(edit.InputSchema, edit.InputSchema, "changes");
                AssertRequired(change, "handle", "expected_layer", "expected_owner_handle", "expected_vertices",
                    "expected_normal", "expected_elevation", "expected_thickness", "expected_constant_width", "expected_is_closed", "edits");
                var vertex = TypedArrayItem(edit.InputSchema, change, "expected_vertices", allowNull: true);
                AssertRequired(vertex, "index", "x", "y", "z", "bulge", "start_width", "end_width");
                var positionEdit = TypedArrayItem(edit.InputSchema, change, "edits", allowNull: true);
                AssertRequired(positionEdit, "index", "position");
                var position = positionEdit.GetProperty("properties").GetProperty("position");
                AssertSchemaType(position, "array", allowNull: true);
                Assert.Equal("number", position.GetProperty("items").GetProperty("type").GetString());
            }
            else Assert.DoesNotContain("dwg_edit_polyline_vertices", names);

            if (hasTextCreation)
            {
                var mtext = Assert.Single(tools, tool => tool.ProtocolTool.Name == "dwg_create_mtext").ProtocolTool;
                AssertRequired(mtext.InputSchema, "text", "location");
                Assert.True(mtext.InputSchema.GetProperty("properties").TryGetProperty("text_style", out _));
            }
            if (readOnly)
                foreach (string mutation in new[] { "dwg_create_entities", "dwg_edit_polyline_vertices",
                    "dwg_create_text", "dwg_create_mtext", "dwg_create_leader", "dwg_set_text_style", "dwg_match_properties",
                    "dwg_create_block_variant", "dwg_update_block_angles", "dwg_set_system_variable", "dwg_run_lisp", "dwg_set_crosshair_settings", "dwg_set_settings" })
                    Assert.DoesNotContain(mutation, names);
        }

        private static JsonElement TypedArrayItem(JsonElement root, JsonElement parent, string field, bool allowNull = false)
        {
            var array = parent.GetProperty("properties").GetProperty(field);
            AssertSchemaType(array, "array", allowNull);
            var item = array.GetProperty("items");
            if (item.TryGetProperty("$ref", out var reference))
            {
                string path = reference.GetString();
                Assert.StartsWith("#/", path, StringComparison.Ordinal);
                item = root;
                foreach (string part in path.Substring(2).Split('/'))
                    item = item.GetProperty(part.Replace("~1", "/").Replace("~0", "~"));
            }
            AssertSchemaType(item, "object", allowNull);
            Assert.True(item.TryGetProperty("properties", out _), "Array items must expose typed fields.");
            return item;
        }

        private static void AssertRequired(JsonElement schema, params string[] fields)
        {
            if (!schema.TryGetProperty("required", out var requiredNode))
            {
                Assert.Empty(fields);
                return;
            }
            var required = requiredNode.EnumerateArray().Select(value => value.GetString()).ToArray();
            if (fields.Length == 0) Assert.Empty(required);
            foreach (string field in fields) Assert.Contains(field, required);
        }
    }
}
