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
        private static bool initialized, busy;

        private sealed class RefreshContext
        {
            internal readonly Document Document;
            // Retain the exact wrapper on which native handlers were registered.
            internal readonly Database Database;
            internal readonly DeferredAngleRefreshState Queue = new DeferredAngleRefreshState();
            internal HashSet<ObjectId> BoundReferences = new HashSet<ObjectId>();
            internal HashSet<ObjectId> EvaluatedDefinitions = new HashSet<ObjectId>();
            internal readonly HashSet<ObjectId> OwnWrites = new HashSet<ObjectId>();
            private long revision;

            internal RefreshContext(Document document, Database database)
            {
                Document = document;
                Database = database;
            }

            internal void Notify(DeferredAngleRefreshNotification kind) => Queue.Notify(++revision, kind);

            internal void ObjectChanged(object sender, ObjectEventArgs args)
            {
                HandleEvent("angle object modified", () =>
                {
                    // Native callbacks only queue; never lock/open/write from here.
                    var value = args?.DBObject;
                    if (value == null || OwnWrites.Contains(value.ObjectId)) return;
                    if (value is BlockReference && BoundReferences.Contains(value.ObjectId) ||
                        (value is LineAngularDimension2 || value is Point3AngularDimension) &&
                        EvaluatedDefinitions.Contains(value.OwnerId))
                        Notify(DeferredAngleRefreshNotification.DimensionChange);
                });
            }
        }

        private sealed class RefreshReadback
        {
            internal string Snapshot;
            internal bool Wrote;
        }

        // Only IDs and managed values cross the read/write transaction boundary.
        private sealed class AttributeRefreshUpdate
        {
            internal ObjectId AttributeId, ReferenceId, DefinitionId;
            internal string Tag, OldText, NewText;
            internal JObject Binding, ExpectedAngles;
        }

        private sealed class RefreshPlan
        {
            internal long StartedAt;
            internal string Snapshot;
            internal readonly List<AttributeRefreshUpdate> Updates = new List<AttributeRefreshUpdate>();
            internal HashSet<ObjectId> BoundReferences = new HashSet<ObjectId>();
            internal HashSet<ObjectId> EvaluatedDefinitions = new HashSet<ObjectId>();
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
                Application.Idle += OnIdle;
                foreach (Document doc in documentManager) Attach(doc);
                initialized = true;
            }
            catch
            {
                Shutdown();
                throw;
            }
        }

        internal static void Shutdown()
        {
            initialized = false;
            HandleEvent("angle idle unsubscribe", () => Application.Idle -= OnIdle);
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
            // AutoCAD/Civil 3D activates the Start tab with Document == null.
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
                database.ObjectModified += context.ObjectChanged;
                context.Notify(DeferredAngleRefreshNotification.ExternalChange);
                // Publish only after every subscription succeeded.
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
        }

        private static void Unsubscribe(RefreshContext context)
        {
            HandleEvent("angle command-ended unsubscribe", () => context.Document.CommandEnded -= OnCommand);
            HandleEvent("angle command-cancelled unsubscribe", () => context.Document.CommandCancelled -= OnCommand);
            HandleEvent("angle command-failed unsubscribe", () => context.Document.CommandFailed -= OnCommand);
            HandleEvent("angle database unsubscribe", () => context.Database.ObjectModified -= context.ObjectChanged);
        }

        private static void OnCreated(object sender, DocumentCollectionEventArgs args)
            => HandleEvent("angle document created", () => Attach(args?.Document));

        private static void OnActivated(object sender, DocumentCollectionEventArgs args)
        {
            HandleEvent("angle document activated", () =>
            {
                var doc = args?.Document;
                if (Attach(doc) && Documents.TryGetValue(doc, out var context))
                    context.Notify(DeferredAngleRefreshNotification.ExternalChange);
            });
        }

        private static void OnDestroyed(object sender, DocumentCollectionEventArgs args)
            => HandleEvent("angle document destroyed", () => Detach(args?.Document));

        private static void OnCommand(object sender, CommandEventArgs args)
        {
            HandleEvent("angle command ended", () =>
            {
                if (!(sender is Document doc) || !Documents.TryGetValue(doc, out var context)) return;
                context.Notify(DeferredAngleRefreshNotification.ExternalChange);
                // UNDO/REDO also raise this event. Never open a write transaction
                // or acquire a write lock from inside their native callbacks.
            });
        }

        private static void OnIdle(object sender, EventArgs args)
        {
            HandleEvent("angle idle", () =>
            {
                var doc = documentManager?.MdiActiveDocument;
                if (doc == null) return;
                if (Attach(doc) && Documents.TryGetValue(doc, out var context)) RunPass(doc, context);
            });
        }

        private static void RunPass(Document doc, RefreshContext context)
        {
            // Keep pending notifications while a tracked LISP owns the document.
            if (busy || LispJobStore.Session.IsBusy) return;
            DeferredAngleRefreshPass ticket = default;
            bool began = false;
            try
            {
                // Native getters can fail during document activation/destruction too.
                if (doc == null || context.Database.IsDisposed || doc != documentManager?.MdiActiveDocument ||
                    doc.IsReadOnly || !doc.Editor.IsQuiescent) return;
                if (!context.Queue.TryBeginPass(Clock.ElapsedMilliseconds, out ticket)) return;
                began = true;
                busy = true;
                var plan = InspectRefresh(context);
                var readback = new RefreshReadback { Snapshot = plan.Snapshot, Wrote = false };
                if (plan.Updates.Count != 0)
                {
                    using (doc.LockDocument())
                    {
                        if (context.Database.IsDisposed || doc != documentManager?.MdiActiveDocument ||
                            doc.IsReadOnly || LispJobStore.Session.IsBusy)
                            throw new InvalidOperationException("active drawing changed before angle attribute refresh");
                        readback = ApplyRefresh(context, plan);
                    }
                }
                context.BoundReferences = plan.BoundReferences;
                context.EvaluatedDefinitions = plan.EvaluatedDefinitions;
                // Complete only AFTER transactions/lock release and their native notifications.
                var outcome = context.Queue.CompletePass(ticket, readback.Snapshot, readback.Wrote, Clock.ElapsedMilliseconds);
                if (outcome == DeferredAngleRefreshOutcome.Exhausted || outcome == DeferredAngleRefreshOutcome.Failed)
                    ReportStopped(doc, "native annotation did not settle within the bounded refresh passes");
            }
            catch (Exception error)
            {
                PluginLog.Error("angle refresh", error);
                if (began && context.Queue.FailPass(ticket, true, Clock.ElapsedMilliseconds) == DeferredAngleRefreshOutcome.Exhausted)
                    ReportStopped(doc, ErrorSanitizer.Sanitize(error.Message));
            }
            finally
            {
                if (began)
                {
                    context.OwnWrites.Clear();
                    busy = false;
                }
            }
        }

        private static void HandleEvent(string phase, Action work)
        {
            try { work(); }
            catch (Exception error) { PluginLog.Error(phase, error); }
        }

        private static void ReportStopped(Document doc, string reason)
        {
            PluginLog.Debug("Angle attribute refresh stopped: " + reason);
            HandleEvent("angle status display", () => doc?.Editor.WriteMessage(
                "\nBimwright: angle attribute synchronization needs attention: " + reason));
        }
    }
}
