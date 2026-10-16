using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    internal static class McpSchemaAssertions
    {
        internal static void AssertSchemaType(JsonElement schema, string expected, bool allowNull = false)
        {
            var type = schema.GetProperty("type");
            if (type.ValueKind == JsonValueKind.String)
            {
                Assert.Equal(expected, type.GetString());
                return;
            }

            Assert.True(allowNull, "Only reference fields may advertise a nullable type union.");
            Assert.Equal(JsonValueKind.Array, type.ValueKind);
            Assert.Equal(new[] { expected, "null" }.OrderBy(x => x, StringComparer.Ordinal),
                type.EnumerateArray().Select(x => x.GetString()).OrderBy(x => x, StringComparer.Ordinal));
        }
    }
}
