using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Blocks
{
    internal static partial class BlockAngleBindingService
    {
        private static readonly Dictionary<Document, RefreshContext> Documents = new Dictionary<Document, RefreshContext>();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private const int RefreshBudgetMilliseconds = 500;
        private static DocumentCollection documentManager;
        private static bool initialized, busy, idleHooked;

        internal sealed class AngleSnapshot
        {
            internal double Rotation;
            internal Dictionary<string, double> Angles;

            internal bool SameAs(AngleSnapshot other) => other != null &&
                Math.Abs(Rotation - other.Rotation) < 1e-9 && Angles.Count == other.Angles.Count &&
                Angles.All(p => other.Angles.TryGetValue(p.Key, out var value) && Math.Abs(p.Value - value) < 1e-9);
        }

        private sealed class RefreshContext
        {
            internal readonly Document Document;
            internal readonly Database Database;
            internal readonly Dictionary<ObjectId, AngleSnapshot> Before = new Dictionary<ObjectId, AngleSnapshot>();
            internal readonly HashSet<ObjectId> Modified = new HashSet<ObjectId>();
            internal readonly HashSet<ObjectId> OwnWrites = new HashSet<ObjectId>();
            internal string LastReportedReason;
            private bool capturing;

            internal RefreshContext(Document document, Database database)
            { Document = document; Database = database; }

            internal void ObjectOpened(object sender, ObjectEventArgs args)
            {
                HandleEvent("angle block opened", () =>
                {
                    var reference = args?.DBObject as BlockReference;
                    if (reference == null || reference.ObjectId.IsNull || capturing ||
                        OwnWrites.Contains(reference.ObjectId) || Before.ContainsKey(reference.ObjectId)) return;
                    // Read only the object supplied by AutoCAD, before its write.
                    // No transaction, owner lookup, binding scan or setter in a reactor.
                    capturing = true;
                    try
                    {
                        var snapshot = ReadAngleSnapshot(reference);
                        if (snapshot != null) Before.Add(reference.ObjectId, snapshot);
                    }
                    finally { capturing = false; }
                });
            }

            internal void ObjectChanged(object sender, ObjectEventArgs args)
            {
                HandleEvent("angle block modified", () =>
                {
                    var reference = args?.DBObject as BlockReference;
                    if (reference == null || OwnWrites.Contains(reference.ObjectId) ||
                        !Before.ContainsKey(reference.ObjectId)) return;
                    Modified.Add(reference.ObjectId);
                    ScheduleIdle();
                });
            }
        }

        private sealed class AttributeRefreshUpdate
        {
            internal ObjectId AttributeId, ReferenceId, DefinitionId;
            internal string Tag, OldText, NewText;
            internal JObject Binding, ExpectedAngles;
            internal double ExpectedRotation;
        }

        private sealed class RefreshPlan
        {
            internal long StartedAt;
            internal readonly List<AttributeRefreshUpdate> Updates = new List<AttributeRefreshUpdate>();
        }

        internal static void Initialize()
        {
            if (initialized) return;
            documentManager = Application.DocumentManager;
            if (documentManager == null) throw new InvalidOperationException("AutoCAD document manager is not ready.");
            try
            {
                documentManager.DocumentCreated += OnCreated;
                documentManager.DocumentActivated += OnActivated;
                documentManager.DocumentToBeDestroyed += OnDestroyed;
                foreach (Document doc in documentManager) Attach(doc);
                initialized = true;
            }
            catch { Shutdown(); throw; }
        }

        internal static void Shutdown()
        {
            initialized = false;
            UnhookIdle();
            var manager = documentManager;
            documentManager = null;
            if (manager != null)
            {
                HandleEvent("angle created unsubscribe", () => manager.DocumentCreated -= OnCreated);
                HandleEvent("angle activated unsubscribe", () => manager.DocumentActivated -= OnActivated);
                HandleEvent("angle destroyed unsubscribe", () => manager.DocumentToBeDestroyed -= OnDestroyed);
            }
            foreach (var doc in Documents.Keys.ToArray()) Detach(doc);
        }

        private static bool Attach(Document doc)
        {
            if (doc == null) return false;
            if (Documents.ContainsKey(doc)) return true;
            RefreshContext context = null;
            try
            {
                var database = doc.Database;
                if (database == null || database.IsDisposed) return false;
                context = new RefreshContext(doc, database);
                doc.CommandEnded += OnCommand;
                doc.CommandCancelled += OnCommand;
                doc.CommandFailed += OnCommand;
                database.ObjectOpenedForModify += context.ObjectOpened;
                database.ObjectModified += context.ObjectChanged;
                Documents.Add(doc, context);
                return true;
            }
            catch (Exception error)
            {
                if (context != null) Unsubscribe(context);
                PluginLog.Error("angle document attach", error);
                return false;
            }
        }

        private static void Detach(Document doc)
        {
            if (doc == null || !Documents.TryGetValue(doc, out var context)) return;
            Documents.Remove(doc);
            Unsubscribe(context);
            if (!Documents.Values.Any(c => c.Modified.Count != 0)) UnhookIdle();
        }

        private static void Unsubscribe(RefreshContext context)
        {
            HandleEvent("angle command-ended unsubscribe", () => context.Document.CommandEnded -= OnCommand);
            HandleEvent("angle command-cancelled unsubscribe", () => context.Document.CommandCancelled -= OnCommand);
            HandleEvent("angle command-failed unsubscribe", () => context.Document.CommandFailed -= OnCommand);
            HandleEvent("angle opened unsubscribe", () => context.Database.ObjectOpenedForModify -= context.ObjectOpened);
            HandleEvent("angle modified unsubscribe", () => context.Database.ObjectModified -= context.ObjectChanged);
        }

        private static void OnCreated(object sender, DocumentCollectionEventArgs args)
            => HandleEvent("angle document created", () => Attach(args?.Document));

        private static void OnActivated(object sender, DocumentCollectionEventArgs args)
        {
            HandleEvent("angle document activated", () =>
            {
                var doc = args?.Document;
                if (Attach(doc) && Documents.TryGetValue(doc, out var context) && context.Modified.Count != 0)
                    ScheduleIdle();
            });
        }

        private static void OnDestroyed(object sender, DocumentCollectionEventArgs args)
            => HandleEvent("angle document destroyed", () => Detach(args?.Document));

        private static void OnCommand(object sender, CommandEventArgs args)
        {
            HandleEvent("angle command ended", () =>
            {
                if (!(sender is Document doc) || !Documents.TryGetValue(doc, out var context)) return;
                foreach (var id in context.Before.Keys.Where(id => !context.Modified.Contains(id)).ToArray())
                    context.Before.Remove(id);
                if (context.Modified.Count != 0) ScheduleIdle();
            });
        }

        private static void ScheduleIdle()
        {
            if (idleHooked) return;
            Application.Idle += OnIdle;
            idleHooked = true;
        }

        private static void UnhookIdle()
        {
            if (!idleHooked) return;
            HandleEvent("angle idle unsubscribe", () => Application.Idle -= OnIdle);
            idleHooked = false;
        }

        private static void OnIdle(object sender, EventArgs args)
        {
            RefreshContext attempted = null;
            try
            {
                if (busy || LispJobStore.Session.IsBusy) return;
                var doc = documentManager?.MdiActiveDocument;
                if (doc == null || !Documents.TryGetValue(doc, out var context) || context.Modified.Count == 0)
                { UnhookIdle(); return; }
                attempted = context;
                if (context.Database.IsDisposed || doc.IsReadOnly)
                { context.Before.Clear(); context.Modified.Clear(); UnhookIdle(); return; }
                if (!doc.Editor.IsQuiescent) return;
                // One deferred attempt for actual object events, never a periodic scan.
                UnhookIdle();
                var changed = context.Modified.ToDictionary(id => id, id => context.Before[id]);
                context.Before.Clear();
                context.Modified.Clear();
                busy = true;
                try
                {
                    var plan = InspectRefresh(context, changed);
                    if (plan.Updates.Count != 0)
                    {
                        using (doc.LockDocument())
                        {
                            if (context.Database.IsDisposed || doc != documentManager?.MdiActiveDocument ||
                                doc.IsReadOnly || LispJobStore.Session.IsBusy)
                                throw new InvalidOperationException("active drawing changed before angle attribute refresh");
                            ApplyRefresh(context, plan);
                        }
                    }
                    context.LastReportedReason = null;
                }
                catch (Exception error)
                {
                    PluginLog.Error("angle refresh", error);
                    var reason = ErrorSanitizer.Sanitize(error.Message);
                    if (context.LastReportedReason != reason)
                    {
                        context.LastReportedReason = reason;
                        HandleEvent("angle status display", () => doc.Editor.WriteMessage(
                            "\nBimwright: could not update rotated block attribute: " + reason));
                    }
                }
                finally { context.OwnWrites.Clear(); busy = false; }
            }
            catch (Exception error)
            {
                // Failed native preflight is also one attempt, not an Idle loop.
                UnhookIdle();
                attempted?.Before.Clear();
                attempted?.Modified.Clear();
                PluginLog.Error("angle idle", error);
            }
        }

        private static void HandleEvent(string phase, Action work)
        {
            try { work(); }
            catch (Exception error) { PluginLog.Error(phase, error); }
        }
    }
}
