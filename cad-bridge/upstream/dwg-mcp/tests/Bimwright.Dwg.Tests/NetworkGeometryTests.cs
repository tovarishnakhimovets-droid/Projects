using System;
using System.Linq;
using Bimwright.Dwg.Plugin.Network;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class NetworkGeometryTests
    {
        [Fact]
        public void Split_preserves_bends_and_orders_parts_along_the_original_path()
        {
            var request = Noding(new[] { P(0, 0), P(3, 0), P(3, 4), P(8, 4) },
                new[] { P(8, 4), P(3, 2), P(0, 0) });
            var parts = Plan(request);
            Assert.Equal(2, parts.Length);
            Assert.Equal("A", parts[0].SourceHandle);
            Assert.Equal("12", parts[0].FromNode);
            Assert.Equal("11", parts[0].ToNode);
            Assert.Equal("11", parts[1].FromNode);
            Assert.Equal("10", parts[1].ToNode);
            AssertPoints(parts[0].Vertices, P(0, 0), P(3, 0), P(3, 2));
            AssertPoints(parts[1].Vertices, P(3, 2), P(3, 4), P(8, 4));
            Assert.Equal(12, parts.Sum(part => part.Length), 9);
        }

        [Fact]
        public void Snapping_uses_exact_authoritative_node_coordinates()
        {
            var request = Noding(new[] { P(0, 0, 7), P(5, 0, 7), P(10, 0, 7) },
                new[] { P(-0.01, 0, 7), P(5, 0.02, 7), P(10.01, 0, 7) }, 0.03);
            var parts = Plan(request);
            AssertPoints(parts[0].Vertices, P(-0.01, 0, 7), P(5, 0.02, 7));
            AssertPoints(parts[1].Vertices, P(5, 0.02, 7), P(10.01, 0, 7));
            Assert.True(parts[0].Vertices.Last().Exact(parts[1].Vertices.First()));
            Assert.True(parts[0].Vertices.First().Exact(request.Nodes[0].ExpectedPosition));
        }

        [Fact]
        public void Splitting_at_an_existing_bend_keeps_the_bend_as_both_exact_endpoints()
        {
            var request = Noding(new[] { P(0, 0), P(5, 0), P(5, 5) },
                new[] { P(0, 0), P(5, 0), P(5, 5) });
            var parts = Plan(request);
            Assert.Equal(2, parts.Length);
            AssertPoints(parts[0].Vertices, P(0, 0), P(5, 0));
            AssertPoints(parts[1].Vertices, P(5, 0), P(5, 5));
        }

        [Theory]
        [InlineData("missing_anchor", "both source endpoints")]
        [InlineData("coincident_nodes", "coincident")]
        [InlineData("unused_node", "every supplied node")]
        [InlineData("wrong_elevation", "both source endpoints")]
        [InlineData("short_piece", "short path segment")]
        [InlineData("nonplanar_source", "parallel to WCS XY")]
        public void Invalid_network_plans_refuse_instead_of_guessing_topology(string scenario, string reason)
        {
            var vertices = new[] { P(0, 0), P(10, 0) };
            double[][] nodes = scenario switch
            {
                "missing_anchor" => new[] { P(0, 0), P(5, 0) },
                "coincident_nodes" => new[] { P(0, 0), P(10, 0), P(10, 0) },
                "unused_node" => new[] { P(0, 0), P(10, 0), P(5, 3) },
                "wrong_elevation" => new[] { P(0, 0, 0.001), P(10, 0, 0.001) },
                "short_piece" => new[] { P(0, 0), P(0.0000005, 0), P(10, 0) },
                "nonplanar_source" => new[] { P(0, 0), P(10, 0, 1) },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            if (scenario == "nonplanar_source") vertices[1] = P(10, 0, 1);
            var error = Assert.Throws<ArgumentException>(() => Plan(Noding(vertices, nodes, 1e-8)));
            Assert.Contains(reason, error.Message);
        }

        [Fact]
        public void Node_close_to_two_nonadjacent_source_segments_is_ambiguous()
        {
            var request = Noding(new[] { P(0, 0), P(10, 0), P(10, 1), P(0, 1) },
                new[] { P(0, 0), P(0, 1), P(5, 0.5) }, 0.6);
            var error = Assert.Throws<ArgumentException>(() => Plan(request));
            Assert.Contains("ambiguous projections", error.Message);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Endpoint_edit_keeps_every_other_vertex_and_input_snapshot(bool start)
        {
            var vertices = new[] { new NetworkPoint(0, 0, 2), new NetworkPoint(5, 0, 2), new NetworkPoint(5, 5, 2) };
            int index = start ? 0 : 2;
            var target = new NetworkPoint(start ? -2 : 5, start ? 0 : 8, 2);
            var moved = NetworkGeometry.MoveEndpoint(vertices, start, vertices[index], target, 1e-6);
            Assert.True(moved[index].Exact(target));
            for (int i = 0; i < vertices.Length; i++)
                if (i != index) Assert.True(moved[i].Exact(vertices[i]));
            Assert.True(vertices[index].Exact(start ? new NetworkPoint(0, 0, 2) : new NetworkPoint(5, 5, 2)));
        }

        [Fact]
        public void Endpoint_edit_requires_exact_old_coincidence_and_retained_elevation()
        {
            var vertices = new[] { new NetworkPoint(0, 0, 0), new NetworkPoint(5, 0, 0) };
            Assert.Contains("coincide exactly", Assert.Throws<ArgumentException>(() =>
                NetworkGeometry.MoveEndpoint(vertices, true, new NetworkPoint(1e-12, 0, 0), new NetworkPoint(-1, 0, 0), 1e-6)).Message);
            Assert.Contains("elevation", Assert.Throws<ArgumentException>(() =>
                NetworkGeometry.MoveEndpoint(vertices, true, vertices[0], new NetworkPoint(-1, 0, 1e-12), 1e-6)).Message);
            Assert.Contains("short path segment", Assert.Throws<ArgumentException>(() =>
                NetworkGeometry.MoveEndpoint(vertices, true, vertices[0], vertices[1], 1e-6)).Message);
        }

        [Theory]
        [InlineData("fingerprint")]
        [InlineData("handle_alias")]
        [InlineData("missing_snapshot")]
        [InlineData("bad_point")]
        [InlineData("bad_tolerance")]
        [InlineData("string_dry_run")]
        public void Request_parser_requires_identity_unique_handles_complete_snapshots_and_finite_units(string scenario)
        {
            var json = NodingJson(new[] { P(0, 0), P(10, 0) }, new[] { P(0, 0), P(10, 0) });
            switch (scenario)
            {
                case "fingerprint": json["expected_fingerprint"] = "unknown"; break;
                case "handle_alias": json["nodes"][0]["handle"] = "00a"; break;
                case "missing_snapshot": ((JObject)json["sources"][0]).Remove("expected_owner_handle"); break;
                case "bad_point": json["nodes"][0]["expected_position"] = new JArray(0, 0); break;
                case "bad_tolerance": json["geometry_tolerance"] = double.NaN; break;
                case "string_dry_run": json["dry_run"] = "true"; break;
            }
            Assert.False(NetworkRequest.TryParse(json, true, out _, out var error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void Request_parser_refuses_comparison_budget_before_planning()
        {
            var vertices = Enumerable.Range(0, 1001).Select(i => P(i, 0)).ToArray();
            var nodes = Enumerable.Range(0, 1000).Select(i => P(i, 0)).ToArray();
            var json = NodingJson(vertices, nodes);
            Assert.False(NetworkRequest.TryParse(json, true, out _, out var error));
            Assert.Contains("comparison budget", error);
        }

        internal static JObject NodingJson(double[][] vertices, double[][] nodes, double snap = 1e-8)
            => new JObject
            {
                ["expected_document"] = @"D:\drawings\network.dwg",
                ["expected_fingerprint"] = "11223344-1122-3344-5566-778899aabbcc",
                ["snap_tolerance"] = snap,
                ["sources"] = new JArray(new JObject
                {
                    ["handle"] = "A", ["expected_type"] = "Polyline", ["expected_layer"] = "PIPE",
                    ["expected_owner_handle"] = "1F", ["expected_vertices"] = JArray.FromObject(vertices)
                }),
                ["nodes"] = new JArray(nodes.Select((position, index) => new JObject
                {
                    ["handle"] = (index + 16).ToString("X"), ["expected_position"] = new JArray(position)
                }))
            };
        private static NetworkRequest Noding(double[][] vertices, double[][] nodes, double snap = 1e-8)
        {
            Assert.True(NetworkRequest.TryParse(NodingJson(vertices, nodes, snap), true, out var request, out var error), error);
            return request;
        }
        private static NetworkPart[] Plan(NetworkRequest request) => NetworkGeometry.Plan(request.Sources, request.Nodes,
            request.SnapTolerance, request.GeometryTolerance, request.MinSegmentLength);
        private static double[] P(double x, double y, double z = 0) => new[] { x, y, z };
        private static void AssertPoints(NetworkPoint[] actual, params double[][] expected)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < actual.Length; i++) Assert.Equal(expected[i], actual[i].ToArray());
        }
    }
}
