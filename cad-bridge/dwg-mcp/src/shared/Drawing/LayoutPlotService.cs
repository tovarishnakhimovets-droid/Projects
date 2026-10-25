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

        internal static JObject Settings(Layout l) => Settings(l, l.LayoutName, l.Handle.ToString());

        private static JObject Settings(PlotSettings l, string layoutName, string layoutHandle) => new JObject
        {
            ["layout_name"] = layoutName, ["layout_handle"] = layoutHandle,
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
            string temporaryDirectory = Path.GetTempPath();
            if (temporaryDirectory.StartsWith(@"\\", StringComparison.Ordinal) ||
                new DriveInfo(Path.GetPathRoot(temporaryDirectory)).DriveType == DriveType.Network)
                throw new IOException("the configured OS temporary directory must be local for PDF plotting");
            string temporary = Path.Combine(temporaryDirectory, ".dwg-plot-" + Guid.NewGuid().ToString("N") + ".pdf");
            string staged = Path.Combine(Path.GetDirectoryName(path), ".dwg-publish-" + Guid.NewGuid().ToString("N") + ".pdf");
            var cleanupWarnings = new List<string>();
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
                        var plotSettingsValidator = PlotSettingsValidator.Current;
                        plotSettingsValidator.RefreshLists(copy);
                        error = LayoutPlotPolicy.ValidateSavedDevice(l.PlotConfigurationName,
                            plotSettingsValidator.GetPlotDeviceList().Cast<string>().ToArray());
                        if (error != null) throw new InvalidOperationException(error);
                        error = LayoutPlotPolicy.ValidateSavedMedia(l.CanonicalMediaName,
                            plotSettingsValidator.GetCanonicalMediaNameList(copy).Cast<string>().ToArray());
                        if (error != null) throw new InvalidOperationException(error);
                        plotSettingsValidator.SetPlotConfigurationName(copy, l.PlotConfigurationName, l.CanonicalMediaName);
                        plotSettingsValidator.RefreshLists(copy);
                        error = LayoutPlotPolicy.ValidateUnchangedSettings(settings, Settings(copy, l.LayoutName, l.Handle.ToString()));
                        if (error != null) throw new InvalidOperationException(error);
                        info.Layout = l.ObjectId;
                        info.OverrideSettings = copy;
                        // MatchDisabled can reject a present exact medium. Matching is allowed only if every saved value remains exact.
                        validator.MediaMatchingPolicy = MatchingPolicy.MatchEnabled;
                        validator.Validate(info);
                        if (info.ValidatedSettings == null) throw new InvalidOperationException("native plot validation returned no settings");
                        error = LayoutPlotPolicy.ValidateUnchangedSettings(settings,
                            Settings(info.ValidatedSettings, l.LayoutName, l.Handle.ToString()));
                        if (error != null) throw new InvalidOperationException(error);
                        if (dryRun) return new { dry_run = true, output_path = path, plot_settings = settings,
                            background_plot_before = Convert.ToInt32(Application.GetSystemVariable("BACKGROUNDPLOT")), foreground_during_print = true };
                        using (var foreground = new ForegroundPlotScope(
                            () => Convert.ToInt32(Application.GetSystemVariable("BACKGROUNDPLOT")),
                            value => Application.SetSystemVariable("BACKGROUNDPLOT", value)))
                        using (var engine = PlotFactory.CreatePublishEngine())
                        using (var page = new PlotPageInfo())
                        using (var progress = new PlotProgressDialog(false, 1, true))
                        {
                            bool progressEnded = false;
                            try
                            {
                                progress.OnBeginPlot();
                                progress.IsVisible = false;
                                progress.OnBeginSheet();
                                engine.BeginPlot(progress, null);
                                engine.BeginDocument(info, doc.Name, null, 1, true, temporary);
                                engine.BeginPage(page, info, true, null);
                                engine.BeginGenerateGraphics(null);
                                engine.EndGenerateGraphics(null);
                                engine.EndPage(null);
                                progress.OnEndSheet();
                                engine.EndDocument(null);
                                progress.OnEndPlot();
                                progressEnded = true;
                                engine.EndPlot(null);
                            }
                            finally
                            {
                                if (!progressEnded) progress.OnEndPlot();
                            }
                        }
                    }
                }
                if (!File.Exists(temporary)) throw new IOException("plot engine did not create the requested PDF");
                byte[] header = new byte[5];
                using (var stream = File.OpenRead(temporary))
                    if (stream.Read(header, 0, header.Length) != header.Length || !LayoutPlotPolicy.HasPdfHeader(header))
                        throw new IOException("plot output is not a PDF");
                long bytes; string hash;
                using (var stream = File.OpenRead(temporary))
                using (var sha = SHA256.Create())
                {
                    bytes = stream.Length;
                    hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }
                File.Copy(temporary, staged, false);
                File.Move(staged, path, overwrite);
                return new { dry_run = false, output_path = path, bytes, sha256 = hash, layout_name = name,
                    pages = 1, background_plot_restored = true, cleanup_warnings = cleanupWarnings };
            }
            finally
            {
                // Both unique files belong only to this operation. Never delete the existing destination first.
                DeleteTemporary(temporary, cleanupWarnings);
                DeleteTemporary(staged, cleanupWarnings);
            }
        }

        private static void DeleteTemporary(string path, List<string> warnings)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { warnings.Add("temporary PDF cleanup failed: " + path + ": " + ex.Message); }
        }
    }
}
