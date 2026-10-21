using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Drawing;
using Bimwright.Dwg.Plugin.Rewriting;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CollapseAndRewriteHandler : IAcadCommand
    {
        public string Name => "collapse_and_rewrite";
        public string Description => "Rewrite explicit text clusters in separate guarded transactions, preserving anchor styling.";
        public CommandSchema Schema => CommandSchemas.CollapseAndRewrite;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!CollapseAndRewriteInput.TryParse(parameters, out var input, out var error)) return CommandResult.Fail(error);
            try
            {
                error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
                if (error != null) return CommandResult.Fail(error);
                if (!LineweightTargetPreflight.TryCheckDrawing(doc, out error)) return CommandResult.Fail(error);
                var results = new List<object>();
                foreach (var request in input.Clusters)
                {
                    // A sibling's committed transaction is retained if a later cluster fails.
                    var result = RewriteExecutor.Execute(doc, input, request);
                    results.Add(ToWire(result));
                }
                return CommandResult.Success(results);
            }
            catch (Exception ex) { return CommandResult.Fail(ErrorSanitizer.Sanitize(ex.Message)); }
        }

        private static object ToWire(RewriteResult result) => new
        {
            anchor_handle = result.AnchorHandle, ok = result.Ok, new_handle = result.NewHandle,
            deleted_count = result.Ok ? (int?)result.DeletedCount : null,
            action = result.Ok ? RewriteActionNames.ToWire(result.Action) : null,
            owner_handle = result.OwnerHandle, owner_name = result.OwnerName,
            owner_is_layout = result.Ok ? (bool?)result.OwnerIsLayout : null,
            final_width = result.Ok ? (double?)result.FinalWidth : null,
            final_text_height = result.Ok ? (double?)result.FinalTextHeight : null,
            error = result.Error
        };
    }
}
