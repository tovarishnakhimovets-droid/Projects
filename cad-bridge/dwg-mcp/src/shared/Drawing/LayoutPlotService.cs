using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Export;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Drawing
{
    internal static class LayoutPlotService
    {
        internal static Layout Open(Database db, Transaction tx, string name)
        {
            var error = LayoutPlotPolicy.ValidateLayoutName(name);
            if (error != null) throw new ArgumentException(error);
            var layouts = (DBDictionary)tx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            if (!layouts.Contains(name)) throw new ArgumentException("layout does not exist: " + name);
            return (Layout)tx.GetObject(layouts.GetAt(name), OpenMode.ForRead);
        }

        internal static JObject Settings(Layout l) => new JObject
        {
            ["layout_name"] = l.LayoutName, ["layout_handle"] = l.Handle.ToString(),
            ["model_type"] = l.ModelType, ["device"] = l.PlotConfigurationName,
            ["media"] = l.CanonicalMediaName, ["style_sheet"] = l.CurrentStyleSheet,
            ["paper_units"] = l.PlotPaperUnits.ToString(), ["rotation"] = l.PlotRotation.ToString(),
            ["plot_type"] = l.PlotType.ToString(),
            ["paper_size"] = new JArray(l.PlotPaperSize.X, l.PlotPaperSize.Y),
            ["origin"] = new JArray(l.PlotOrigin.X, l.PlotOrigin.Y),
            ["window"] = new JArray(l.PlotWindowArea.MinPoint.X, l.PlotWindowArea.MinPoint.Y, l.PlotWindowArea.MaxPoint.X, l.PlotWindowArea.MaxPoint.Y),
            ["use_standard_scale"] = l.UseStandardScale, ["standard_scale"] = l.StdScaleType.ToString(),
            ["custom_scale"] = new JArray(l.CustomPrintScale.Numerator, l.CustomPrintScale.Denominator),
            ["centered"] = l.PlotCentered, ["plot_with_styles"] = l.PlotPlotStyles,
            ["print_lineweights"] = l.PrintLineweights, ["scale_lineweights"] = l.ScaleLineweights
        };

        internal static object List(Document doc)
        {
            var rows = new List<object>();
            using (var tx = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var dict = (DBDictionary)tx.GetObject(doc.Database.LayoutDictionaryId, OpenMode.ForRead);
                if (dict.Count > 256) throw new InvalidOperationException("drawing has more than 256 layouts");
                foreach (DBDictionaryEntry item in dict)
                {
                    var l = (Layout)tx.GetObject(item.Value, OpenMode.ForRead);
                    rows.Add(new { name = l.LayoutName, handle = l.Handle.ToString(),
                        block_record_handle = l.BlockTableRecordId.Handle.ToString(), tab_order = l.TabOrder,
                        is_current = string.Equals(LayoutManager.Current.CurrentLayout, l.LayoutName, StringComparison.Ordinal),
                        plot_settings = Settings(l) });
                }
            }
            return new { layouts = rows };
        }

        internal static object Entities(Document doc, string name, string type, string layer, int limit, bool geometry)
        {
            var error = LayoutPlotPolicy.ValidateEntityFilter(type, limit);
            if (error != null) throw new ArgumentException(error);
            var result = new List<object>();
            int characters = 0;
            var watch = Stopwatch.StartNew();
            int scanned = 0; bool complete = true;
            using (var tx = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var l = Open(doc.Database, tx, name);
                var space = (BlockTableRecord)tx.GetObject(l.BlockTableRecordId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    if (++scanned > 50000 || watch.ElapsedMilliseconds > 3000) { complete = false; break; }
                    var e = tx.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null || (type != null && e.GetType().Name != type) || (layer != null && !string.Equals(layer, e.Layer, StringComparison.OrdinalIgnoreCase))) continue;
                    if (result.Count == limit) { complete = false; break; }
                    var description = JObject.FromObject(CadEntityProperties.Describe(e, tx, geometry));
                    int length = description.ToString(Newtonsoft.Json.Formatting.None).Length;
                    if (characters + length > 200000) { complete = false; break; }
                    characters += length;
                    result.Add(description);
                }
            }
            return new { layout_name = name, entities = result, count = result.Count, scanned_count = scanned, complete, truncated = !complete };
        }

        internal static object Select(Document doc, string name, string expectedLayout, bool dryRun)
        {
            using (var tx = doc.Database.TransactionManager.StartOpenCloseTransaction()) Open(doc.Database, tx, name);
            string current = LayoutManager.Current.CurrentLayout;
            if (!string.Equals(current, expectedLayout, StringComparison.Ordinal))
                throw new InvalidOperationException("current layout changed; reread drawing info");
            if (!dryRun)
            {
                LayoutManager.Current.CurrentLayout = name;
                if (!doc.Database.TileMode) doc.Editor.SwitchToPaperSpace();
            }
            bool paperActive = false;
            if (!doc.Database.TileMode)
                using (var tx = doc.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var viewport = (Viewport)tx.GetObject(doc.Database.PaperSpaceVportId, OpenMode.ForRead);
                    paperActive = doc.Database.CurrentSpaceId == viewport.OwnerId;
                }
            return new { dry_run = dryRun, previous_layout = current,
                current_layout = LayoutManager.Current.CurrentLayout, target_layout = name,
                paper_space_active = paperActive };
        }

        internal static object Plot(Document doc, string name, JObject expectedSettings, string output, bool overwrite, bool allowRepo, bool dryRun)
        {
            string path = ExportPathPolicy.ValidateAndNormalize(output, ".pdf", overwrite, allowRepo, out var error);
            if (error != null) throw new ArgumentException(error);
            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting) throw new InvalidOperationException("AutoCAD is already plotting");
            if (!string.Equals(LayoutManager.Current.CurrentLayout, name, StringComparison.Ordinal))
                throw new InvalidOperationException("requested layout must already be current; call dwg_select_layout explicitly");
            string temporary = Path.Combine(Path.GetDirectoryName(path), ".dwg-plot-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                using (var tx = doc.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var l = Open(doc.Database, tx, name);
                    var settings = Settings(l);
                    error = LayoutPlotPolicy.ValidateSnapshot(expectedSettings, settings);
                    if (error != null) throw new InvalidOperationException(error);
                    if (!LayoutPlotPolicy.IsPdfDevice(l.PlotConfigurationName))
                        throw new InvalidOperationException("saved layout device is not a PDF device; configure PDF printing in the source drawing first");
                    if (!l.ModelType)
                    {
                        var vp = (Viewport)tx.GetObject(doc.Database.PaperSpaceVportId, OpenMode.ForRead);
                        if (doc.Database.CurrentSpaceId != vp.OwnerId) throw new InvalidOperationException("paper space must be active, not a floating viewport");
                    }
                    using (var copy = new PlotSettings(l.ModelType))
                    using (var info = new PlotInfo())
                    using (var validator = new PlotInfoValidator())
                    {
                        copy.CopyFrom(l);
                        info.Layout = l.ObjectId;
                        info.OverrideSettings = copy;
                        validator.MediaMatchingPolicy = MatchingPolicy.MatchDisabled;
                        validator.Validate(info);
                        if (dryRun) return new { dry_run = true, output_path = path, plot_settings = settings };
                        using (var engine = PlotFactory.CreatePublishEngine())
                        using (var page = new PlotPageInfo())
                        {
                            engine.BeginPlot(null, null);
                            engine.BeginDocument(info, doc.Name, null, 1, true, temporary);
                            engine.BeginPage(page, info, true, null);
                            engine.BeginGenerateGraphics(null);
                            engine.EndGenerateGraphics(null);
                            engine.EndPage(null);
                            engine.EndDocument(null);
                            engine.EndPlot(null);
                        }
                    }
                }
                if (!File.Exists(temporary)) throw new IOException("plot engine did not create the requested PDF");
                byte[] header = new byte[5];
                using (var stream = File.OpenRead(temporary))
                    if (stream.Read(header, 0, header.Length) != header.Length || !LayoutPlotPolicy.HasPdfHeader(header))
                        throw new IOException("plot output is not a PDF");
                File.Move(temporary, path, overwrite);
                using (var stream = File.OpenRead(path))
                using (var sha = SHA256.Create())
                    return new { dry_run = false, output_path = path, bytes = stream.Length,
                        sha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(), layout_name = name, pages = 1 };
            }
            finally
            {
                // This unique file belongs only to this operation. The existing destination is never deleted first.
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
