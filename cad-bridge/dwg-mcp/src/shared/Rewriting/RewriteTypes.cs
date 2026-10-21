using System;
using System.Collections.Generic;

namespace Bimwright.Dwg.Plugin.Rewriting
{
    public enum RewriteAction { Update, Collapse, RewriteInBlock }

    public static class RewriteActionNames
    {
        public const string Update = "update";
        public const string Collapse = "collapse";
        public const string RewriteInBlock = "rewrite_in_block";

        public static string ToWire(RewriteAction action)
        {
            switch (action)
            {
                case RewriteAction.Update: return Update;
                case RewriteAction.Collapse: return Collapse;
                case RewriteAction.RewriteInBlock: return RewriteInBlock;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
    }

    // All geometry is expressed in the owning block record's coordinates.
    public sealed class RewriteRequest
    {
        public string AnchorHandle { get; set; }
        public List<string> DeleteHandles { get; set; } = new List<string>();
        public string NewText { get; set; }
        public bool ConvertToMText { get; set; }
        public double? MtextWidth { get; set; }
        public double? TextHeight { get; set; }
        public bool CreatesMText => ConvertToMText || DeleteHandles.Count > 0;
    }

    public sealed class RewriteResult
    {
        public bool Ok { get; set; }
        public string AnchorHandle { get; set; }
        public string NewHandle { get; set; }
        public RewriteAction Action { get; set; }
        public int DeletedCount { get; set; }
        public string OwnerHandle { get; set; }
        public string OwnerName { get; set; }
        public bool OwnerIsLayout { get; set; }
        public double FinalWidth { get; set; }
        public double FinalTextHeight { get; set; }
        public string Error { get; set; }
    }
}
