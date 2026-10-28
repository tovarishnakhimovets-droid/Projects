using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    // Evidence for our synchronous lock acquisition only. This does not suppress
    // any DrawingRevision event, nor prove equality of the whole database.
    internal static class LockBoundaryProof
    {
        internal static bool TryValidate(JObject diagnostics, string expectedSession, long expectedRevision,
            long actualRevision, out string[] affectedHandles, out string error)
        {
            affectedHandles = Array.Empty<string>();
            error = "lock acquisition events are not a complete unchanged-network candidate set";
            if (diagnostics == null || string.IsNullOrEmpty(expectedSession) || expectedRevision < 0 ||
                actualRevision <= expectedRevision || actualRevision - expectedRevision > 16 ||
                !Scalar(diagnostics, "session", JTokenType.String, expectedSession) ||
                !Integer(diagnostics, "current_revision", actualRevision) ||
                !Integer(diagnostics, "after_revision", expectedRevision) ||
                !Integer(diagnostics, "unrecorded_count", 0) ||
                !Integer(diagnostics, "diagnostic_capture_failures", 0) ||
                !Scalar(diagnostics, "eventbuffer_truncated", JTokenType.Boolean, false) ||
                !(diagnostics["events"] is JArray events) || events.Count != actualRevision - expectedRevision)
                return false;
            var handles = new SortedSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < events.Count; i++)
            {
                if (!(events[i] is JObject item) || !Integer(item, "revision", expectedRevision + i + 1) ||
                    !Scalar(item, "kind", JTokenType.String, "modified") ||
                    !Scalar(item, "class", JTokenType.String, "Autodesk.AutoCAD.DatabaseServices.AssocNetwork") ||
                    !Scalar(item, "phase", JTokenType.String, "write_lock_acquire") ||
                    !Scalar(item, "flags_complete", JTokenType.Boolean, true)) return false;
                foreach (var flag in new[] { "is_modified", "is_modified_xdata", "is_modified_graphics",
                    "is_new_object", "is_undoing", "is_erase_status_toggled" })
                    if (!Scalar(item, flag, JTokenType.Boolean, false)) return false;
                var handle = item["handle"]?.Type == JTokenType.String ? (string)item["handle"] : null;
                if (string.IsNullOrEmpty(handle) || handle.Length > 16 || handle[0] == '0' ||
                    handle.Any(c => !((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))) return false;
                handles.Add(handle);
            }
            affectedHandles = handles.ToArray();
            error = null;
            return true;
        }

        private static bool Scalar(JObject value, string key, JTokenType type, object expected)
            => value[key]?.Type == type && JToken.DeepEquals(value[key], JToken.FromObject(expected));
        private static bool Integer(JObject value, string key, long expected)
            => Scalar(value, key, JTokenType.Integer, expected);
    }
}
