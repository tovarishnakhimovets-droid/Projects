using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Cad
{
    // Host-free validation shared by the MCP method and the native selection reader.
    public sealed class SelectionReadInput
    {
        public const int DefaultMaxEntities = 100;
        public const int MaximumMaxEntities = 500;

        private SelectionReadInput(int maxEntities, bool includeGeometry)
        {
            MaxEntities = maxEntities;
            IncludeGeometry = includeGeometry;
        }

        public int MaxEntities { get; }
        public bool IncludeGeometry { get; }

        public static bool TryParse(JToken parameters, out SelectionReadInput input, out string error)
        {
            input = null;
            error = null;
            if (!(parameters is JObject obj))
            {
                error = "get_selection params must be an object";
                return false;
            }

            foreach (var field in obj.Properties())
            {
                if (field.Name != "max_entities" && field.Name != "include_geometry")
                {
                    error = "unknown get_selection field: " + field.Name;
                    return false;
                }
            }

            var maxEntities = DefaultMaxEntities;
            var limitToken = obj["max_entities"];
            if (limitToken != null &&
                (limitToken.Type != JTokenType.Integer ||
                 !int.TryParse(limitToken.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out maxEntities) ||
                 maxEntities < 1 || maxEntities > MaximumMaxEntities))
            {
                error = "max_entities must be an integer between 1 and 500";
                return false;
            }

            var includeGeometry = false;
            var geometryToken = obj["include_geometry"];
            if (geometryToken != null)
            {
                if (geometryToken.Type != JTokenType.Boolean)
                {
                    error = "include_geometry must be a boolean";
                    return false;
                }
                includeGeometry = geometryToken.Value<bool>();
            }

            input = new SelectionReadInput(maxEntities, includeGeometry);
            return true;
        }
    }

    // The full selection count and the bounded returned rows have different scopes.
    // This bookkeeping contains no AutoCAD types, so partial results can be tested offline.
    internal sealed class SelectionReadSummary
    {
        internal SelectionReadSummary(int selectedCount, int maxEntities)
        {
            if (selectedCount < 0) throw new ArgumentOutOfRangeException(nameof(selectedCount));
            if (maxEntities < 1 || maxEntities > SelectionReadInput.MaximumMaxEntities)
                throw new ArgumentOutOfRangeException(nameof(maxEntities));
            SelectedCount = selectedCount;
            ReturnLimit = Math.Min(selectedCount, maxEntities);
        }

        internal int SelectedCount { get; }
        internal int ReturnLimit { get; }
        internal int ReturnedCount { get; private set; }
        internal int Errors { get; private set; }
        internal bool Truncated => SelectedCount > ReturnLimit;
        internal bool Complete => ReturnedCount == SelectedCount && Errors == 0;
        internal Dictionary<string, int> TypeCounts { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        internal void Add(string entityType, bool ok)
        {
            if (ReturnedCount >= ReturnLimit)
                throw new InvalidOperationException("selection return limit exceeded");
            ReturnedCount++;
            if (!ok) Errors++;
            if (!string.IsNullOrEmpty(entityType))
            {
                TypeCounts.TryGetValue(entityType, out var count);
                TypeCounts[entityType] = count + 1;
            }
        }
    }
}
