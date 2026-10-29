using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.View
{
    internal sealed class DrawingRevision
    {
        private static readonly object Gate = new object();
        private static ConditionalWeakTable<Document, DrawingRevision> states =
            new ConditionalWeakTable<Document, DrawingRevision>();
        private static readonly HashSet<DrawingRevision> LiveStates = new HashSet<DrawingRevision>();
        private static DocumentCollection documents;
        private Database database;
        private long revision;
        private int graphicsReadDepth;
        private long graphicsRevision;
        private readonly object diagnosticsGate = new object();
        private readonly Queue<JObject> recentEvents = new Queue<JObject>();
        private long diagnosticCaptureFailures;
        private DiagnosticPhase activePhase;
        private const int EventCapacity = 16;
        internal string SessionId { get; } = Guid.NewGuid().ToString("N");
        internal long Revision => Interlocked.Read(ref revision);
        internal long GraphicsRevision => Interlocked.Read(ref graphicsRevision);

        // Phases describe synchronous native work only. They never suppress events
        // or authorize a changed revision. The caller still enforces its own guard.
        internal IDisposable BeginPhase(string name)
        {
            lock (diagnosticsGate)
            {
                var scope = new DiagnosticPhase(this, activePhase, Bounded(name ?? "unscoped", 64));
                Volatile.Write(ref activePhase, scope);
                return scope;
            }
        }

        internal JObject DiagnosticsSince(long afterRevision)
        {
            if (afterRevision < 0) throw new ArgumentOutOfRangeException(nameof(afterRevision));
            lock (diagnosticsGate)
            {
                var events = new JArray();
                foreach (var item in recentEvents)
                    if (item.Value<long>("revision") > afterRevision) events.Add(item.DeepClone());
                long currentRevision = Revision;
                long requestedEvents = currentRevision > afterRevision ? currentRevision - afterRevision : 0;
                long unrecordedCount = Math.Max(0, requestedEvents - events.Count);
                return new JObject
                {
                    ["session"] = SessionId, ["current_revision"] = currentRevision,
                    ["after_revision"] = afterRevision, ["graphics_revision"] = GraphicsRevision,
                    ["capacity"] = EventCapacity, ["captures_per_phase"] = EventCapacity,
                    ["active_phase"] = activePhase == null ? JValue.CreateNull() : new JValue(activePhase.Name),
                    ["eventbuffer_truncated"] = unrecordedCount > 0, ["unrecorded_count"] = unrecordedCount,
                    ["diagnostic_capture_failures"] = diagnosticCaptureFailures, ["events"] = events
                };
            }
        }

        private sealed class DiagnosticPhase : IDisposable
        {
            private DrawingRevision owner;
            private readonly DiagnosticPhase previous;
            internal readonly string Name;
            internal readonly int ThreadId;
            internal int CaptureAttempts;
            internal DiagnosticPhase(DrawingRevision owner, DiagnosticPhase previous, string name)
            { this.owner = owner; this.previous = previous; Name = name; ThreadId = Thread.CurrentThread.ManagedThreadId; }
            public void Dispose()
            {
                var state = Interlocked.Exchange(ref owner, null);
                if (state == null) return;
                lock (state.diagnosticsGate) Volatile.Write(ref state.activePhase, previous);
            }
        }

        // Only the synchronous native preview/view+regen calls enter this scope.
        // It is not a blanket suppression: data writes and structural events still
        // invalidate history even when a native reactor performs them inside a read.
        internal IDisposable BeginGraphicsRead()
        {
            Interlocked.Increment(ref graphicsReadDepth);
            return new GraphicsRead(this);
        }

        private sealed class GraphicsRead : IDisposable
        {
            private DrawingRevision owner;
            internal GraphicsRead(DrawingRevision owner) => this.owner = owner;
            public void Dispose()
            {
                var state = Interlocked.Exchange(ref owner, null);
                if (state != null) Interlocked.Decrement(ref state.graphicsReadDepth);
            }
        }
        internal static DrawingRevision For(Document document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            lock (Gate)
            {
                if (documents == null)
                {
                    documents = Application.DocumentManager;
                    documents.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
                }
                return states.GetValue(document, Create);
            }
        }

        private static DrawingRevision Create(Document document)
        {
            // Document.Database can return a fresh managed wrapper on every read.
            // Key by the document lifetime and retain the wrapper owning our handlers.
            var state = new DrawingRevision { database = document.Database };
            state.database.ObjectModified += state.OnObjectModified;
            state.database.ObjectAppended += state.OnObjectAppended;
            state.database.ObjectErased += state.OnObjectErased;
            state.database.ObjectUnappended += state.OnObjectUnappended;
            state.database.ObjectReappended += state.OnObjectReappended;
            LiveStates.Add(state);
            return state;
        }

        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs args)
        {
            lock (Gate)
            {
                if (!states.TryGetValue(args.Document, out var state)) return;
                state.Detach();
                states.Remove(args.Document);
                LiveStates.Remove(state);
            }
        }

        internal static void Shutdown()
        {
            lock (Gate)
            {
                foreach (var state in LiveStates) state.Detach();
                LiveStates.Clear();
                states = new ConditionalWeakTable<Document, DrawingRevision>();
                if (documents != null)
                    documents.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
                documents = null;
            }
        }

        private void Detach()
        {
            database.ObjectModified -= OnObjectModified;
            database.ObjectAppended -= OnObjectAppended;
            database.ObjectErased -= OnObjectErased;
            database.ObjectUnappended -= OnObjectUnappended;
            database.ObjectReappended -= OnObjectReappended;
            // AutoCAD owns this database; do not dispose it.
            database = null;
        }

        private void OnObjectAppended(object sender, ObjectEventArgs args) => Record("appended", args.DBObject);
        private void OnObjectUnappended(object sender, ObjectEventArgs args) => Record("unappended", args.DBObject);
        private void OnObjectReappended(object sender, ObjectEventArgs args) => Record("reappended", args.DBObject);
        private void OnObjectErased(object sender, ObjectErasedEventArgs args) => Record("erased", args.DBObject);

        private void OnObjectModified(object sender, ObjectEventArgs args)
        {
            var value = args.DBObject;
            // Navigation itself updates view state; its full metadata is checked
            // independently. All other observed database-object changes invalidate history.
            if (value is ViewportTableRecord || value is Viewport) return;
            try
            {
                // IsModifiedGraphics alone also includes real entity changes.
                // IsModified reports AssertWriteEnabled; require it and every
                // other persistent-change flag to be false before accepting a
                // graphics-only notification from our bounded rendering call.
                if (Volatile.Read(ref graphicsReadDepth) > 0 && value is Entity &&
                    value.IsModifiedGraphics && !value.IsModified && !value.IsModifiedXData &&
                    !value.IsNewObject && !value.IsUndoing && !value.IsEraseStatusToggled)
                {
                    Interlocked.Increment(ref graphicsRevision);
                    return;
                }
            }
            catch
            {
                // Unreadable native flags are unknown changes, never proof of a read.
            }
            Record("modified", value);
        }

        private void Record(string kind, DBObject value)
        {
            // Keep exactly the old invalidation semantics. Diagnostics are best effort
            // and must not throw back into native event dispatch or change the counter.
            long eventRevision = Interlocked.Increment(ref revision);
            // Ordinary drawing work pays no native diagnostic getter/JSON cost. Each
            // explicit phase captures only its first 16 events, even for a huge bind.
            var trace = Volatile.Read(ref activePhase);
            if (trace == null || trace.ThreadId != Thread.CurrentThread.ManagedThreadId ||
                Volatile.Read(ref trace.CaptureAttempts) >= EventCapacity ||
                Interlocked.Increment(ref trace.CaptureAttempts) > EventCapacity) return;
            try
            {
                var flags = new JObject
                {
                    ["is_modified"] = Flag(() => value.IsModified),
                    ["is_modified_xdata"] = Flag(() => value.IsModifiedXData),
                    ["is_modified_graphics"] = Flag(() => value.IsModifiedGraphics),
                    ["is_new_object"] = Flag(() => value.IsNewObject),
                    ["is_undoing"] = Flag(() => value.IsUndoing),
                    ["is_erase_status_toggled"] = Flag(() => value.IsEraseStatusToggled)
                };
                bool flagsComplete = true;
                foreach (var item in flags.Properties()) if (item.Value.Type == JTokenType.Null) flagsComplete = false;
                var entry = new JObject
                {
                    ["revision"] = eventRevision, ["kind"] = kind,
                    ["class"] = Text(() => value.GetType().FullName, 128),
                    ["handle"] = Text(() => value.Handle.ToString(), 64),
                    ["flags_complete"] = flagsComplete
                };
                foreach (var item in flags.Properties()) entry[item.Name] = item.Value.DeepClone();
                lock (diagnosticsGate)
                {
                    entry["phase"] = trace.Name;
                    recentEvents.Enqueue(entry);
                    if (recentEvents.Count > EventCapacity) recentEvents.Dequeue();
                }
            }
            catch
            {
                lock (diagnosticsGate) diagnosticCaptureFailures++;
            }
        }

        private static JToken Flag(Func<bool> read)
        { try { return new JValue(read()); } catch { return JValue.CreateNull(); } }
        private static JToken Text(Func<string> read, int limit)
        { try { var value = read(); return value == null ? JValue.CreateNull() : new JValue(Bounded(value, limit)); } catch { return JValue.CreateNull(); } }
        private static string Bounded(string value, int limit) => value.Length <= limit ? value : value.Substring(0, limit);
    }
}
