using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Blocks
{
    internal static class BlockVariantService
    {
        internal static JObject Create(Document doc, BlockVariantInput input)
        {
            var db = doc.Database;
            var watch = Stopwatch.StartNew();
            var mapped = new JObject();
            var source = input.ExpectedReference;
            var createdIds = new List<ObjectId>();
            bool cloneStarted = false;
            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    Require(DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint) == null, "active document identity changed");
                    Require(!doc.IsReadOnly, "drawing is read-only");
                    var reference = Open<BlockReference>(tx, db, (string)source["handle"]);
                    Require(reference.OwnerId == db.CurrentSpaceId && reference.OwnerId.Handle.ToString().Equals((string)source["owner_handle"], StringComparison.OrdinalIgnoreCase), "source owner changed or not current space");
                    Require(reference.Layer == (string)source["layer"], "source layer changed");
                    CheckLayer(tx, db, reference.Layer);
                    Near(reference.Position, source["position"], input.GeometryTolerance, "source position changed");
                    Require(reference.Normal == Vector3d.ZAxis && Point(source["normal"]) == new Point3d(0,0,1), "only +Z block references are supported");
                    Require(reference.ScaleFactors == new Scale3d(1) && Point(source["scale"]) == new Point3d(1,1,1), "only unscaled source references are supported");
                    Require(Math.Abs(reference.Rotation - (double)source["rotation_radians"]) <= input.GeometryTolerance, "source rotation changed");
                    Require(reference.IsDynamicBlock, "source must be dynamic");
                    var definition = (BlockTableRecord)tx.GetObject(reference.DynamicBlockTableRecord, OpenMode.ForRead);
                    Require(definition.Name == (string)source["effective_name"] && definition.Handle.ToString().Equals((string)source["definition_handle"], StringComparison.OrdinalIgnoreCase), "source definition changed");
                    Require(!definition.IsDependent && !definition.IsFromExternalReference && !definition.IsLayout && !definition.IsAnonymous, "external/layout/anonymous definition unsupported");
                    var table = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForRead);
                    Require(!table.Has(input.NewName), "new block name already exists");
                    var children = new HashSet<ObjectId>(); foreach (ObjectId id in definition) { Require(children.Count < 500, "definition entity limit exceeded"); children.Add(id); }
                    var attrs = CheckAttributes(tx, reference, (JArray)source["attributes"], input.GeometryTolerance);
                    var angles = ReadAngles(reference);
                    CheckAngles(angles, (JObject)source["dynamic_values"], input.GeometryTolerance);
                    var edited = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
                    foreach (JObject edit in input.PolylineEdits)
                    {
                        var poly = Open<Polyline>(tx, db, (string)edit["handle"]);
                        Require(children.Contains(poly.ObjectId) && poly.Layer == (string)edit["expected_layer"], "polyline owner/layer changed");
                        Require(poly.NumberOfVertices == 2 && !poly.Closed && poly.Normal == Vector3d.ZAxis && poly.Thickness == 0 && poly.ConstantWidth == 0, "only simple open planar two-vertex polylines supported");
                        for (int i=0;i<2;i++) { Near(poly.GetPoint3dAt(i), edit["expected_vertices"][i]["point"], input.GeometryTolerance, "polyline geometry changed"); Require(poly.GetBulgeAt(i)==0 && poly.GetStartWidthAt(i)==0 && poly.GetEndWidthAt(i)==0, "polyline width/bulge changed"); }
                        Require(Point(edit["new_vertices"][0]).DistanceTo(Point(edit["new_vertices"][1])) > input.GeometryTolerance, "new segment too short");
                        edited.Add((string)edit["handle"], poly.ObjectId);
                    }
                    foreach (JObject edit in input.AttributeEdits)
                    {
                        var a = Open<AttributeDefinition>(tx, db, (string)edit["handle"]);
                        Require(children.Contains(a.ObjectId) && a.Layer == (string)edit["expected_layer"] && a.Tag == (string)edit["tag"] && a.TextString == (string)edit["expected_default"], "attribute definition snapshot changed");
                        Require(!a.HasFields && !a.IsMTextAttributeDefinition, "field or multiline default editing unsupported");
                        edited.Add((string)edit["handle"], a.ObjectId);
                    }
                    foreach (JObject edit in input.MTextEdits)
                    {
                        var t = Open<MText>(tx, db, (string)edit["handle"]);
                        Require(children.Contains(t.ObjectId) && t.Layer == (string)edit["expected_layer"] && t.Contents == (string)edit["expected_text"] && !t.HasFields, "MText snapshot changed or has fields");
                        Near(t.Location, edit["expected_location"], input.GeometryTolerance, "MText location changed");
                        edited.Add((string)edit["handle"], t.ObjectId);
                    }
                    Time(watch);
                    if (input.DryRun) return new JObject { ["dry_run"]=true, ["committed"]=false, ["source_handle"]=(string)source["handle"], ["new_name"]=input.NewName, ["polyline_count"]=input.PolylineEdits.Count, ["attribute_default_count"]=input.AttributeEdits.Count, ["attribute_count"]=attrs.Count, ["saved"]=false };
                    // Clone the defining BTR, not Wblock(blockId), which would export its entities as model space.
                    using (var temporary = new Database(true, true))
                    {
                        var firstMap = new IdMapping();
                        db.WblockCloneObjects(new ObjectIdCollection(new[] {definition.ObjectId}), temporary.BlockTableId, firstMap, DuplicateRecordCloning.Ignore, false);
                        var tempDefinitionId = Mapped(firstMap, definition.ObjectId);
                        var tempEdits = edited.ToDictionary(p=>p.Key,p=>Mapped(firstMap,p.Value),StringComparer.OrdinalIgnoreCase);
                        using (var tempTx = temporary.TransactionManager.StartTransaction())
                        {
                            var tempDefinition = (BlockTableRecord)tempTx.GetObject(tempDefinitionId, OpenMode.ForWrite);
                            tempDefinition.Name = input.NewName;
                            Require(tempDefinition.IsDynamicBlock, "native clone lost the dynamic definition");
                            foreach (JObject edit in input.PolylineEdits)
                            {
                                var p = (Polyline)tempTx.GetObject(tempEdits[(string)edit["handle"]], OpenMode.ForWrite);
                                for(int i=0;i<2;i++) { var v=Point(edit["new_vertices"][i]); p.SetPointAt(i,new Point2d(v.X,v.Y)); }
                                p.Elevation=Point(edit["new_vertices"][0]).Z;
                            }
                            foreach (JObject edit in input.AttributeEdits) ((AttributeDefinition)tempTx.GetObject(tempEdits[(string)edit["handle"]],OpenMode.ForWrite)).TextString=(string)edit["new_default"];
                            foreach (JObject edit in input.MTextEdits) ((MText)tempTx.GetObject(tempEdits[(string)edit["handle"]],OpenMode.ForWrite)).Contents=(string)edit["new_text"];
                            tempTx.Commit();
                        }
                        Time(watch);
                        Require(DocumentGuard.Validate(doc,input.ExpectedDocument,input.ExpectedFingerprint)==null, "active document identity changed before write");
                        var secondMap = new IdMapping();
                        cloneStarted = true;
                        temporary.WblockCloneObjects(new ObjectIdCollection(new[]{tempDefinitionId}), db.BlockTableId, secondMap, DuplicateRecordCloning.Ignore, false);
                        foreach(IdPair pair in secondMap) if(pair.IsCloned && !pair.Value.IsNull) createdIds.Add(pair.Value);
                        var newDefinitionId=Mapped(secondMap,tempDefinitionId);
                        var newDefinition=(BlockTableRecord)tx.GetObject(newDefinitionId,OpenMode.ForWrite);
                        Require(newDefinition.Name==input.NewName && newDefinition.IsDynamicBlock && newDefinitionId != definition.ObjectId, "independent dynamic definition validation failed");
                        foreach(var p in tempEdits) mapped[p.Key]=Mapped(secondMap,p.Value).Handle.ToString();
                        newDefinition.UpdateAnonymousBlocks();
                        var owner=(BlockTableRecord)tx.GetObject(db.CurrentSpaceId,OpenMode.ForWrite);
                        var newRef=new BlockReference(Point(input.InsertionPoint),newDefinitionId);
                        newRef.SetDatabaseDefaults(db); newRef.LayerId=reference.LayerId; newRef.Color=reference.Color;
                        newRef.LinetypeId=reference.LinetypeId; newRef.LinetypeScale=reference.LinetypeScale; newRef.LineWeight=reference.LineWeight; newRef.Transparency=reference.Transparency;
                        newRef.Rotation=reference.Rotation;
                        owner.AppendEntity(newRef); tx.AddNewlyCreatedDBObject(newRef,true); createdIds.Add(newRef.ObjectId);
                        Require(newRef.IsDynamicBlock, "inserted reference is not dynamic");
                        var newProps=ReadAngles(newRef);
                        Require(newProps.Keys.OrderBy(k=>k).SequenceEqual(angles.Keys.OrderBy(k=>k)),"native clone changed writable angular properties");
                        foreach(DynamicBlockReferenceProperty p in newRef.DynamicBlockReferencePropertyCollection) if(!p.ReadOnly && p.UnitsType==DynamicBlockReferencePropertyUnitsType.Angular) p.Value=angles[p.PropertyName];
                        CheckAngles(ReadAngles(newRef),(JObject)source["dynamic_values"],input.GeometryTolerance);
                        var displacement=Matrix3d.Displacement(newRef.Position-reference.Position);
                        foreach(var a in attrs)
                        {
                            // Clone the attribute itself to retain presentation/flags; source fields were refused above.
                            var clone=(AttributeReference)a.Clone(); clone.TransformBy(displacement);
                            if(input.AttributeValues.TryGetValue(a.Tag,StringComparison.OrdinalIgnoreCase,out var value)) clone.TextString=(string)value;
                            newRef.AttributeCollection.AppendAttribute(clone); tx.AddNewlyCreatedDBObject(clone,true); createdIds.Add(clone.ObjectId);
                        }
                        Require(newRef.AttributeCollection.Count==attrs.Count,"new reference lost attributes");
                        Time(watch);
                        var result=new JObject { ["dry_run"]=false,["committed"]=true,["source_handle"]=(string)source["handle"],["new_name"]=input.NewName,["handle"]=newRef.Handle.ToString(),["definition_handle"]=newDefinition.Handle.ToString(),["mapped_entities"]=mapped,["dynamic"]=true,["attribute_count"]=attrs.Count,["saved"]=false,["native_graph_contents_validated"]=false,["note"]="Native whole-definition clone preserves graph ownership; grip/action behavior still requires live verification. Clone APIs may use internal transactions; failure cleanup is explicit and best effort." };
                        tx.Commit();
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                // Native cloning can use internal transactions. Never promise transaction rollback alone.
                var cleanupErrors=new JArray();
                if(cloneStarted)
                {
                    try
                    {
                        using(var clean=db.TransactionManager.StartTransaction())
                        {
                            var table=(BlockTable)clean.GetObject(db.BlockTableId,OpenMode.ForRead);
                            if(table.Has(input.NewName))
                            {
                                var own=(BlockTableRecord)clean.GetObject(table[input.NewName],OpenMode.ForWrite);
                                // Only the newly reserved name can be erased; preflight proved it absent.
                                foreach(ObjectId id in own.GetBlockReferenceIds(true,false)) if(!id.IsErased) clean.GetObject(id,OpenMode.ForWrite).Erase();
                                own.Erase();
                            }
                            foreach(var id in createdIds.AsEnumerable().Reverse())
                                if(!id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased)
                                    try { clean.GetObject(id,OpenMode.ForWrite).Erase(); } catch(Exception e) {cleanupErrors.Add(ErrorSanitizer.Sanitize(e.Message));}
                            clean.Commit();
                        }
                    }
                    catch(Exception e){cleanupErrors.Add(ErrorSanitizer.Sanitize(e.Message));}
                }
                throw new ArgumentException(ErrorSanitizer.Sanitize(ex.Message)+(cloneStarted?"; native-clone cleanup errors: "+cleanupErrors.ToString(Newtonsoft.Json.Formatting.None):"; no destination clone started"));
            }
        }
        internal static T Open<T>(Transaction tx,Database db,string handle) where T:DBObject
        {
            Require(CadHandleResolver.TryResolve(db,handle,out var id,out var error),error??"handle unavailable");
            Require(!id.IsErased && !id.IsEffectivelyErased,"object erased");
            var value=tx.GetObject(id,OpenMode.ForRead) as T; Require(value!=null,"object class mismatch: "+handle); return value;
        }
        internal static Dictionary<string,double> ReadAngles(BlockReference reference)
        {
            var values=new Dictionary<string,double>(StringComparer.Ordinal);
            foreach(DynamicBlockReferenceProperty p in reference.DynamicBlockReferencePropertyCollection)
                if(!p.ReadOnly && p.UnitsType==DynamicBlockReferencePropertyUnitsType.Angular)
                { Require(p.Value is double && !values.ContainsKey(p.PropertyName),"angular property is ambiguous or unsupported"); values.Add(p.PropertyName,(double)p.Value); }
            Require(values.Count<=16,"angle count exceeded"); return values;
        }
        internal static void CheckAngles(Dictionary<string,double> actual,JObject expected,double tolerance)
        { Require(actual.Count==expected.Count,"angular property membership changed"); foreach(var p in expected.Properties()) Require(actual.TryGetValue(p.Name,out var n)&&Math.Abs(n-(double)p.Value)<=tolerance,"angular property changed: "+p.Name); }
        private static List<AttributeReference> CheckAttributes(Transaction tx,BlockReference reference,JArray expected,double tolerance)
        {
            Require(reference.AttributeCollection.Count==expected.Count,"attribute membership changed");
            var result=new List<AttributeReference>();
            foreach(ObjectId id in reference.AttributeCollection)
            {
                var a=(AttributeReference)tx.GetObject(id,OpenMode.ForRead);
                var e=expected.OfType<JObject>().SingleOrDefault(v=>((string)v["handle"]).Equals(a.Handle.ToString(),StringComparison.OrdinalIgnoreCase));
                Require(e!=null && a.Tag==(string)e["tag"] && a.TextString==(string)e["text"],"attribute snapshot changed");
                Require(!a.HasFields && !(bool)e["fields_present"] && !a.IsMTextAttribute && !(bool)e["is_mtext_attribute"],"source field/multiline attributes unsupported");
                Require(a.Invisible==(bool)e["invisible"]&&a.IsConstant==(bool)e["constant"]&&a.IsPreset==(bool)e["preset"]&&a.IsVerifiable==(bool)e["verifiable"]&&a.LockPositionInBlock==(bool)e["lock_position_in_block"],"attribute flags changed");
                Near(a.Position,e["position"],tolerance,"attribute position changed"); Near(a.AlignmentPoint,e["alignment_point"],tolerance,"attribute alignment changed");
                Require(Math.Abs(a.Height-(double)e["height"])<=tolerance&&Math.Abs(a.Rotation-(double)e["rotation_radians"])<=tolerance,"attribute text geometry changed"); result.Add(a);
            }
            return result;
        }
        internal static void CheckLayer(Transaction tx,Database db,string name)
        { var t=(LayerTable)tx.GetObject(db.LayerTableId,OpenMode.ForRead); Require(t.Has(name),"layer unavailable"); var l=(LayerTableRecord)tx.GetObject(t[name],OpenMode.ForRead); Require(!l.IsLocked&&!l.IsDependent,"locked/dependent layer unsupported"); }
        private static ObjectId Mapped(IdMapping map,ObjectId source)
        { foreach(IdPair pair in map) if(pair.Key==source&&!pair.Value.IsNull) return pair.Value; throw new ArgumentException("native clone mapping incomplete"); }
        internal static Point3d Point(JToken p)=>new Point3d((double)p["x"],(double)p["y"],(double)p["z"]);
        internal static void Near(Point3d actual,JToken p,double tolerance,string error)=>Require(actual.DistanceTo(Point(p))<=tolerance,error);
        internal static void Require(bool ok,string message){if(!ok)throw new ArgumentException(message);}
        private static void Time(Stopwatch watch)=>Require(watch.ElapsedMilliseconds<15000,"15 second native operation budget exceeded");
    }
}
