// Minimal host doubles for the real read-only text-style handler and metadata copier.
// Plugin builds still compile against Autodesk references; no AutoCAD process is loaded here.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Autodesk.AutoCAD.DatabaseServices
{
    public enum OpenMode { ForRead, ForWrite }

    public readonly struct Handle
    {
        private readonly long value;
        public Handle(long value) { this.value = value; }
        public override string ToString() => value.ToString("X", CultureInfo.InvariantCulture);
    }

    public readonly struct ObjectId : IEquatable<ObjectId>
    {
        public ObjectId(long value) { Value = value; }
        public long Value { get; }
        public bool IsNull => Value == 0;
        public static ObjectId Null => default;
        public bool Equals(ObjectId other) => Value == other.Value;
        public override bool Equals(object other) => other is ObjectId id && Equals(id);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(ObjectId first, ObjectId second) => first.Equals(second);
        public static bool operator !=(ObjectId first, ObjectId second) => !first.Equals(second);
    }

    public class TextStyleTable : DBObject, IEnumerable
    {
        public List<ObjectId> Ids { get; } = new List<ObjectId>();
        public IEnumerator GetEnumerator() => Ids.GetEnumerator();
    }

    public class TextStyleTableRecord : DBObject
    {
        public string Name { get; set; }
        public Handle Handle { get; set; }
        public string FileName { get; set; }
        public string BigFontFileName { get; set; }
        public double TextSize { get; set; }
        public double XScale { get; set; }
        public double ObliquingAngle { get; set; }
        public bool IsVertical { get; set; }
        public bool IsShapeFile { get; set; }
        public bool IsDependent { get; set; }
        public Exception FontReadError { get; set; }
        private Autodesk.AutoCAD.GraphicsInterface.FontDescriptor font;
        public Autodesk.AutoCAD.GraphicsInterface.FontDescriptor Font
        {
            get => FontReadError == null ? font : throw FontReadError;
            set => font = value;
        }
    }

    public partial class Database
    {
        public ObjectId TextStyleTableId { get; set; } = new ObjectId(1);
        public ObjectId Textstyle { get; set; }
        public TransactionManager TransactionManager { get; } = new TransactionManager();
    }

    public class TransactionManager
    {
        public Dictionary<ObjectId, DBObject> Objects { get; } = new Dictionary<ObjectId, DBObject>();
        public List<(ObjectId Id, OpenMode Mode)> Opens { get; } = new List<(ObjectId, OpenMode)>();
        public int StartedCount { get; private set; }
        public int OpenCloseStartedCount { get; private set; }
        public int CommitCount { get; set; }
        public Transaction StartTransaction()
        {
            StartedCount++;
            return new Transaction(this);
        }
        public Transaction StartOpenCloseTransaction()
        {
            OpenCloseStartedCount++;
            return new Transaction(this);
        }
    }

    public class Transaction : IDisposable
    {
        private readonly TransactionManager manager;
        public Transaction(TransactionManager manager) { this.manager = manager; }
        public DBObject GetObject(ObjectId id, OpenMode mode)
        {
            manager.Opens.Add((id, mode));
            return manager.Objects[id];
        }
        public void Commit() => manager.CommitCount++;
        public void Dispose() { }
    }
}

namespace Autodesk.AutoCAD.GraphicsInterface
{
    public readonly struct FontDescriptor
    {
        public FontDescriptor(string typeFace, bool bold, bool italic, int characterSet, int pitchAndFamily)
        {
            TypeFace = typeFace;
            Bold = bold;
            Italic = italic;
            CharacterSet = characterSet;
            PitchAndFamily = pitchAndFamily;
        }
        public string TypeFace { get; }
        public bool Bold { get; }
        public bool Italic { get; }
        public int CharacterSet { get; }
        public int PitchAndFamily { get; }
    }
}
