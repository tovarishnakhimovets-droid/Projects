using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Bimwright.Dwg.Plugin.View;

namespace Bimwright.Dwg.Plugin
{
    // A local, read-only proof for one lock boundary, not an alternative DWG
    // revision. Networks outside the default hierarchy are never inferred safe.
    internal sealed class AssocNetworkSnapshot
    {
        private const int MaxNetworks = 128;
        private const int MaxActions = 10000;
        private const int MaxBytes = 2 * 1024 * 1024;
        private const int BudgetMilliseconds = 3000;
        private readonly Dictionary<string, byte[]> networks;
        private readonly string documentSession;
        private readonly string rootHandle;
        internal int NetworkCount => networks.Count;
        internal long TotalBytes { get; }

        private AssocNetworkSnapshot(string session, string root, Dictionary<string, byte[]> values, long bytes)
        { documentSession = session; rootHandle = root; networks = values; TotalBytes = bytes; }

        // The caller owns the document lock and checks revision stability around
        // this complete capture. No transaction object or native wrapper escapes.
        internal static AssocNetworkSnapshot Capture(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            var budget = new CaptureBudget();
            var values = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<ObjectId>();
            var pending = new Queue<ObjectId>();
            var database = doc.Database;
            string session = DrawingRevision.For(doc).SessionId;
            string rootHandle = null;
            using (var tx = database.TransactionManager.StartOpenCloseTransaction())
            {
                budget.Check();
                var root = AssocNetwork.GetInstanceFromDatabase(database, false, null);
                if (!root.IsNull) { rootHandle = root.Handle.ToString(); pending.Enqueue(root); }
                int actionCount = 0;
                while (pending.Count != 0)
                {
                    budget.Check();
                    ObjectId id = pending.Dequeue();
                    if (!visited.Add(id)) continue;
                    if (id.IsNull || !id.IsValid || id.IsErased || id.IsEffectivelyErased ||
                        !database.Equals(id.Database))
                        throw new InvalidOperationException("associative snapshot contains an unavailable or foreign network");
                    var value = tx.GetObject(id, OpenMode.ForRead);
                    if (!(value is AssocNetwork network) || value.GetType() != typeof(AssocNetwork) ||
                        value.IsAProxy || !string.Equals(value.GetRXClass()?.Name, "AcDbAssocNetwork", StringComparison.Ordinal))
                        throw new InvalidOperationException("associative snapshot requires an exact native AssocNetwork");
                    if (values.Count >= MaxNetworks)
                        throw new InvalidOperationException("associative snapshot network limit exceeded");
                    string handle = id.Handle.ToString();
                    using (var filer = new SnapshotFiler(budget))
                    {
                        network.DwgOut(filer);
                        budget.Check();
                        values.Add(handle, filer.Finish());
                    }
                    // This collection is native internal storage. Copy it before
                    // opening any child or doing anything that might reenter CAD.
                    ObjectIdCollection actions = network.GetActions;
                    int count = actions.Count;
                    if (count > MaxActions - actionCount)
                        throw new InvalidOperationException("associative snapshot action limit exceeded");
                    actionCount += count;
                    var copy = new ObjectId[count];
                    actions.CopyTo(copy, 0);
                    foreach (ObjectId action in copy)
                    {
                        budget.Check();
                        if (action.IsNull || !action.IsValid || action.IsErased || action.IsEffectivelyErased ||
                            !database.Equals(action.Database))
                            throw new InvalidOperationException("associative snapshot contains an unavailable or foreign action");
                        var objectClass = action.ObjectClass;
                        if (objectClass == null)
                            throw new InvalidOperationException("associative snapshot action class is unavailable");
                        if (objectClass.IsDerivedFrom(RXObject.GetClass(typeof(AssocNetwork))))
                        {
                            if (!string.Equals(objectClass.Name, "AcDbAssocNetwork", StringComparison.Ordinal))
                                throw new InvalidOperationException("associative snapshot contains an unsupported derived network");
                            pending.Enqueue(action);
                        }
                    }
                }
            }
            budget.Check();
            return new AssocNetworkSnapshot(session, rootHandle, values, budget.Bytes);
        }

        internal bool Matches(Document doc, IEnumerable<string> affectedHandles, out string error)
        {
            error = null;
            try
            {
                if (doc == null || affectedHandles == null ||
                    !string.Equals(documentSession, DrawingRevision.For(doc).SessionId, StringComparison.Ordinal))
                    throw new InvalidOperationException("associative snapshot document/session mismatch");
                int count = 0;
                foreach (string handle in affectedHandles)
                {
                    if (++count > MaxNetworks || handle == null || !networks.ContainsKey(handle))
                        throw new InvalidOperationException("lock event network was not present in the complete pre-lock snapshot");
                }
                if (count == 0)
                    throw new InvalidOperationException("associative snapshot requires explicit affected network handles");
                var current = Capture(doc);
                if (!string.Equals(rootHandle, current.rootHandle, StringComparison.OrdinalIgnoreCase) ||
                    networks.Count != current.networks.Count)
                    throw new InvalidOperationException("associative network topology changed at the lock boundary");
                foreach (var pair in networks)
                    if (!current.networks.TryGetValue(pair.Key, out var hash) || !SameBytes(pair.Value, hash))
                        throw new InvalidOperationException("native associative network data changed at the lock boundary");
                return true;
            }
            catch (System.Exception ex)
            {
                string message = ex.Message ?? ex.GetType().Name;
                error = message.Length <= 512 ? message : message.Substring(0, 512);
                return false;
            }
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private sealed class CaptureBudget
        {
            private readonly Stopwatch watch = Stopwatch.StartNew();
            internal long Bytes { get; private set; }
            internal void Check()
            {
                if (watch.ElapsedMilliseconds > BudgetMilliseconds)
                    throw new InvalidOperationException("associative snapshot time budget exceeded");
            }
            internal void Reserve(int bytes)
            {
                Check();
                if (bytes < 0 || bytes > MaxBytes - Bytes)
                    throw new InvalidOperationException("associative snapshot byte limit exceeded");
                Bytes += bytes;
            }
        }

        // FileFiler selects the native persistent-storage representation. Every
        // write has a distinct type tag; sizes and raw floating-point bits are
        // retained. This never invokes DwgIn or writes to the drawing database.
        private sealed class SnapshotFiler : DwgFiler
        {
            private readonly CaptureBudget budget;
            private readonly MemoryStream data = new MemoryStream();
            private readonly BinaryWriter writer;
            private string fault;
            private int writtenRecords;
            private ErrorStatus status = ErrorStatus.OK;
            internal SnapshotFiler(CaptureBudget budget)
            {
                this.budget = budget;
                writer = new BinaryWriter(data, Encoding.UTF8, true);
                Write(0, 12, w =>
                {
                    w.Write((int)Autodesk.AutoCAD.DatabaseServices.DwgVersion.Current);
                    w.Write((int)MaintenanceReleaseVersion.ReleaseCurrent);
                    w.Write((int)MaintenanceReleaseVersion.ReleaseExtendedCurrent);
                });
            }
            public override FilerType FilerType => FilerType.FileFiler;
            public override FullDwgVersion DwgVersion => new FullDwgVersion(
                Autodesk.AutoCAD.DatabaseServices.DwgVersion.Current, MaintenanceReleaseVersion.ReleaseCurrent);
            public override MaintenanceReleaseVersion ExtendedMinorVersion => MaintenanceReleaseVersion.ReleaseExtendedCurrent;
            public override long Position => data.Position;
            public override ErrorStatus FilerStatus
            {
                get => status;
                set
                {
                    if (value != ErrorStatus.OK)
                    { status = value; if (fault == null) fault = "native filer returned a non-success status"; }
                    else if (fault == null) status = value;
                }
            }
            public override void ResetFilerStatus() { if (fault == null) status = ErrorStatus.OK; }

            internal byte[] Finish()
            {
                budget.Check();
                if (fault != null || status != ErrorStatus.OK)
                    throw new InvalidOperationException(fault ?? "native associative filing failed");
                if (writtenRecords <= 1)
                    throw new InvalidOperationException("native associative filer produced no object fields");
                writer.Flush();
                using (var hash = SHA256.Create()) return hash.ComputeHash(data.GetBuffer(), 0, checked((int)data.Length));
            }
            private void Write(byte tag, int payloadSize, Action<BinaryWriter> emit)
            {
                if (fault != null) throw new InvalidOperationException(fault);
                try
                {
                    budget.Reserve(checked(payloadSize + 1));
                    writer.Write(tag);
                    emit(writer);
                    writtenRecords++;
                }
                catch (System.Exception ex)
                {
                    status = ErrorStatus.InvalidInput;
                    if (fault == null) fault = "incomplete associative filing: " + ex.GetType().Name;
                    throw;
                }
            }
            private void ObjectIdValue(byte tag, ObjectId value) => Write(tag, 17, w =>
            {
                bool absent = value.IsNull;
                w.Write(absent);
                // ObjectId's native identity disambiguates equal handles from
                // different databases in this one process. No pointer is read.
                w.Write(absent ? 0L : value.OldIdPtr.ToInt64());
                w.Write(absent ? 0L : value.Handle.Value);
            });
            private void Bytes(byte tag, byte[] value)
            {
                int count = value == null ? -1 : value.Length;
                if (count > MaxBytes) Unsupported<bool>("oversized byte array");
                Write(tag, checked(4 + Math.Max(0, count)), w =>
                { w.Write(count); if (value != null) w.Write(value); });
            }
            private T Unsupported<T>(string operation)
            {
                fault = "unsupported associative filer operation: " + operation;
                status = ErrorStatus.InvalidInput;
                throw new InvalidOperationException(fault);
            }
            public override void WriteHardOwnershipId(ObjectId value) => ObjectIdValue(1, value);
            public override void WriteSoftOwnershipId(ObjectId value) => ObjectIdValue(2, value);
            public override void WriteHardPointerId(ObjectId value) => ObjectIdValue(3, value);
            public override void WriteSoftPointerId(ObjectId value) => ObjectIdValue(4, value);
            public override void WriteString(string value)
            {
                if (value == null) { Write(5, 4, w => w.Write(-1)); return; }
                if (value.Length > MaxBytes / 2) Unsupported<bool>("oversized string");
                int count = value.Length * 2;
                // Preserve UTF-16 code units, including unpaired surrogates.
                // UTF-8 replacement fallback could make unequal native strings hash equally.
                Write(5, 4 + count, w =>
                { w.Write(value.Length); foreach (char item in value) w.Write((ushort)item); });
            }
            public override void WriteHandle(Handle value) => Write(6, 8, w => w.Write(value.Value));
            public override void WriteInt64(long value) => Write(7, 8, w => w.Write(value));
            public override void WriteInt32(int value) => Write(8, 4, w => w.Write(value));
            public override void WriteInt16(short value) => Write(9, 2, w => w.Write(value));
            public override void WriteUInt64(ulong value) => Write(10, 8, w => w.Write(value));
            public override void WriteUInt32(uint value) => Write(11, 4, w => w.Write(value));
            public override void WriteUInt16(ushort value) => Write(12, 2, w => w.Write(value));
            public override void WriteByte(byte value) => Write(13, 1, w => w.Write(value));
            public override void WriteBoolean(bool value) => Write(14, 1, w => w.Write(value));
            public override void WriteDouble(double value) => Write(15, 8, w => w.Write(value));
            public override void WritePoint2d(Point2d value) => Write(16, 16, w => { w.Write(value.X); w.Write(value.Y); });
            public override void WritePoint3d(Point3d value) => Write(17, 24, w => { w.Write(value.X); w.Write(value.Y); w.Write(value.Z); });
            public override void WriteVector2d(Vector2d value) => Write(18, 16, w => { w.Write(value.X); w.Write(value.Y); });
            public override void WriteVector3d(Vector3d value) => Write(19, 24, w => { w.Write(value.X); w.Write(value.Y); w.Write(value.Z); });
            public override void WriteScale3d(Scale3d value) => Write(20, 24, w => { w.Write(value.X); w.Write(value.Y); w.Write(value.Z); });
            public override void WriteBytes(byte[] value) => Bytes(21, value);
            public override void WriteBinaryChunk(byte[] value) => Bytes(22, value);
            public override void WriteAddress(IntPtr value) => Unsupported<bool>("write address");
            public override void Seek(long offset, int method) => Unsupported<bool>("seek");
            public override ObjectId ReadHardOwnershipId() => Unsupported<ObjectId>("read hard ownership id");
            public override ObjectId ReadSoftOwnershipId() => Unsupported<ObjectId>("read soft ownership id");
            public override ObjectId ReadHardPointerId() => Unsupported<ObjectId>("read hard pointer id");
            public override ObjectId ReadSoftPointerId() => Unsupported<ObjectId>("read soft pointer id");
            public override string ReadString() => Unsupported<string>("read string");
            public override Handle ReadHandle() => Unsupported<Handle>("read handle");
            public override long ReadInt64() => Unsupported<long>("read int64");
            public override int ReadInt32() => Unsupported<int>("read int32");
            public override short ReadInt16() => Unsupported<short>("read int16");
            public override ulong ReadUInt64() => Unsupported<ulong>("read uint64");
            public override uint ReadUInt32() => Unsupported<uint>("read uint32");
            public override ushort ReadUInt16() => Unsupported<ushort>("read uint16");
            public override byte ReadByte() => Unsupported<byte>("read byte");
            public override bool ReadBoolean() => Unsupported<bool>("read boolean");
            public override double ReadDouble() => Unsupported<double>("read double");
            public override Point2d ReadPoint2d() => Unsupported<Point2d>("read point2d");
            public override Point3d ReadPoint3d() => Unsupported<Point3d>("read point3d");
            public override Vector2d ReadVector2d() => Unsupported<Vector2d>("read vector2d");
            public override Vector3d ReadVector3d() => Unsupported<Vector3d>("read vector3d");
            public override Scale3d ReadScale3d() => Unsupported<Scale3d>("read scale3d");
            public override void ReadBytes(byte[] value) => Unsupported<bool>("read bytes");
            public override byte[] ReadBinaryChunk() => Unsupported<byte[]>("read binary chunk");
            public override IntPtr ReadAddress() => Unsupported<IntPtr>("read address");
        }
    }
}
