using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Bimwright.Dwg.Plugin.Blocks
{
    // Persisted per independent definition; fields are not fabricated from undocumented Parameter(n) indices.
    internal static partial class BlockAngleBindingService
    {
        private const string Key="BIMWRIGHT_ANGLE_ATTRIBUTE_V1";
        internal static JObject Change(Document doc,BlockAngleInput input)
        {
            var o=input.Request;var db=doc.Database;
            using(var tx=db.TransactionManager.StartTransaction())
            {
                BlockVariantService.Require(DocumentGuard.Validate(doc,input.ExpectedDocument,input.ExpectedFingerprint)==null,"drawing identity changed");
                BlockVariantService.Require(!doc.IsReadOnly,"drawing read-only");
                var reference=BlockVariantService.Open<BlockReference>(tx,db,(string)o["handle"]);
                BlockVariantService.Require(reference.IsDynamicBlock&&reference.OwnerId==db.CurrentSpaceId,"requires current-space dynamic reference");
                BlockVariantService.CheckLayer(tx,db,reference.Layer);
                var definition=(BlockTableRecord)tx.GetObject(reference.DynamicBlockTableRecord,OpenMode.ForRead);
                BlockVariantService.Require(definition.Name==(string)o["expected_name"]&&!definition.IsDependent&&!definition.IsFromExternalReference,"definition changed or external");
                var angles=BlockVariantService.ReadAngles(reference);BlockVariantService.CheckAngles(angles,(JObject)o["expected_angles"],1e-9);
                var binding=o["binding"] as JObject;AttributeReference attribute=null;
                var existing=ReadBinding(tx,definition);
                bool replacing=false;
                if(binding!=null)
                {
                    var expectedBinding=o["expected_binding"] as JObject;
                    if(expectedBinding==null)BlockVariantService.Require(existing==null,"definition already has an angle binding; supply its exact expected_binding to replace it");
                    else {BlockVariantService.Require(existing!=null&&JToken.DeepEquals(existing,expectedBinding),"existing angle binding changed or missing");replacing=true;}
                    attribute=Attribute(tx,reference,(string)binding["tag"]);
                    BlockVariantService.Require(attribute!=null&&attribute.TextString==(string)binding["expected_text"]&&!attribute.HasFields&&!attribute.IsMTextAttribute,"binding attribute changed, missing or already contains a field");
                    BlockVariantService.CheckLayer(tx,db,attribute.Layer);
                }
                string previewText=null;
                if(binding!=null)previewText=Calculate(tx,reference,angles,binding);
                if(input.DryRun)return new JObject{["dry_run"]=true,["committed"]=false,["saved"]=false,["binding_scope"]="all references of the guarded definition",["binding_replaced"]=replacing,["current_dimension_attribute_text"]=previewText};
                if(((JObject)o["new_angles"]).Count>0)reference.UpgradeOpen();
                foreach(DynamicBlockReferenceProperty p in reference.DynamicBlockReferencePropertyCollection)
                    if(((JObject)o["new_angles"]).TryGetValue(p.PropertyName,out var v))
                    {
                        BlockVariantService.Require(!p.ReadOnly&&p.UnitsType==DynamicBlockReferencePropertyUnitsType.Angular,"target property is not a writable angle");
                        var desired=(double)v;var allowed=p.GetAllowedValues();
                        BlockVariantService.Require(allowed.Length==0||allowed.OfType<double>().Any(n=>Math.Abs(n-desired)<1e-9),"new angle not an allowed value");p.Value=desired;
                    }
                var after=BlockVariantService.ReadAngles(reference);
                foreach(var p in ((JObject)o["new_angles"]).Properties())BlockVariantService.Require(Math.Abs(after[p.Name]-(double)p.Value)<1e-9,"native angle setter did not retain requested value");
                if(binding!=null)
                {
                    definition.UpgradeOpen();if(definition.ExtensionDictionary.IsNull)definition.CreateExtensionDictionary();
                    var dictionary=(DBDictionary)tx.GetObject(definition.ExtensionDictionary,OpenMode.ForWrite);
                    var persistent=(JObject)binding.DeepClone();persistent.Remove("expected_text");
                    // Keep the existing owned record when replacing; no orphan or stale-pointer record.
                    if(replacing)
                    {var record=(Xrecord)tx.GetObject(dictionary.GetAt(Key),OpenMode.ForWrite);record.Data=new ResultBuffer(new TypedValue((int)DxfCode.Text,persistent.ToString(Formatting.None)));}
                    else
                    {var record=new Xrecord{Data=new ResultBuffer(new TypedValue((int)DxfCode.Text,persistent.ToString(Formatting.None)))};dictionary.SetAt(Key,record);tx.AddNewlyCreatedDBObject(record,true);}
                    attribute.UpgradeOpen();attribute.TextString=Calculate(tx,reference,after,persistent);
                }
                else
                {
                    if(existing!=null){attribute=Attribute(tx,reference,(string)existing["tag"]);BlockVariantService.Require(attribute!=null&&!attribute.HasFields,"bound attribute unavailable");attribute.UpgradeOpen();attribute.TextString=Calculate(tx,reference,after,existing);}
                }
                var result=new JObject{["dry_run"]=false,["committed"]=true,["handle"]=reference.Handle.ToString(),["angles"]=JObject.FromObject(after),["attribute_text"]=attribute?.TextString,["binding_created"]=binding!=null&&!replacing,["binding_replaced"]=replacing,["saved"]=false,["binding_requires_plugin"]=true,["grip_undo_behavior_verified"]=false};
                tx.Commit();Initialize();return result;
            }
        }
        private static string Calculate(Transaction tx,BlockReference reference,Dictionary<string,double> angles,JObject b,
            Func<bool> continueInspection=null)
        {
            if((string)b["mode"]=="dimension_display_degrees")
            {
                var native=BlockAngularDimensionReader.ReadCurrent(tx,reference,500,continueInspection);
                BlockVariantService.Require(native.Value<bool>("complete")&&native.Value<int>("matching_count")==1&&native["unique_numeric_text"]?.Type==JTokenType.String,"evaluated angular dimension is missing, ambiguous or unsupported: "+(string)native["error"]);
                return (string)native["unique_numeric_text"];
            }
            var secondary=b["secondary_property"];
            return BlockAngleInput.Format(angles[(string)b["primary_property"]],secondary==null||secondary.Type==JTokenType.Null?0:angles[(string)secondary],(string)b["mode"],(int)b["precision"]);
        }
        private static AttributeReference Attribute(Transaction tx,BlockReference reference,string tag)
        {
            AttributeReference match=null;
            foreach(ObjectId id in reference.AttributeCollection)
            {var a=tx.GetObject(id,OpenMode.ForRead) as AttributeReference;if(a!=null&&a.Tag.Equals(tag,StringComparison.OrdinalIgnoreCase)){BlockVariantService.Require(match==null,"ambiguous attribute tag");match=a;}}
            return match;
        }
        private static JObject ReadBinding(Transaction tx,BlockTableRecord definition)
        {
            if(definition.ExtensionDictionary.IsNull)return null;
            var dict=(DBDictionary)tx.GetObject(definition.ExtensionDictionary,OpenMode.ForRead);
            if(!dict.Contains(Key))return null;
            var record=tx.GetObject(dict.GetAt(Key),OpenMode.ForRead) as Xrecord;
            BlockVariantService.Require(record!=null,"angle binding record has wrong class");
            using(var data=record.Data)
            {var values=data?.AsArray();BlockVariantService.Require(values!=null&&values.Length==1&&values[0].Value is string s&&s.Length<=4096,"angle binding record invalid");return JObject.Parse((string)values[0].Value);}
        }
        private static AngleSnapshot ReadAngleSnapshot(BlockReference reference) =>
            reference.IsDynamicBlock ? new AngleSnapshot
            { Rotation = reference.Rotation, Angles = BlockVariantService.ReadAngles(reference) } : null;

        private static RefreshPlan InspectRefresh(RefreshContext context, Dictionary<ObjectId, AngleSnapshot> changed)
        {
            var db = context.Database;
            var plan = new RefreshPlan { StartedAt = Clock.ElapsedMilliseconds };
            BlockVariantService.Require(changed.Count <= 1000, "rotated block refresh reference limit exceeded");
            using (var tx = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var item in changed)
                {
                    RequireRefreshBudget(plan);
                    var id = item.Key;
                    if (id.IsNull || !id.IsValid || id.IsErased || id.IsEffectivelyErased) continue;
                    var reference = tx.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (reference == null) continue;
                    var current = ReadAngleSnapshot(reference);
                    // MOVE, colour, graphics and other non-angular changes stop here.
                    if (current == null || item.Value.SameAs(current)) continue;
                    var definition = (BlockTableRecord)tx.GetObject(reference.DynamicBlockTableRecord, OpenMode.ForRead);
                    if (definition.IsDependent || definition.IsFromExternalReference) continue;
                    var binding = ReadBinding(tx, definition);
                    if (binding == null) continue;
                    var attribute = Attribute(tx, reference, (string)binding["tag"]);
                    BlockVariantService.Require(attribute != null && !attribute.HasFields && !attribute.IsMTextAttribute,
                        "bound attribute missing or contains a field");
                    var wanted = Calculate(tx, reference, current.Angles, binding,
                        () => Clock.ElapsedMilliseconds - plan.StartedAt < RefreshBudgetMilliseconds);
                    if (attribute.TextString == wanted) continue;
                    BlockVariantService.CheckLayer(tx, db, attribute.Layer);
                    BlockVariantService.CheckLayer(tx, db, reference.Layer);
                    plan.Updates.Add(new AttributeRefreshUpdate
                    {
                        AttributeId = attribute.ObjectId,
                        ReferenceId = reference.ObjectId,
                        DefinitionId = definition.ObjectId,
                        Tag = attribute.Tag,
                        OldText = attribute.TextString,
                        NewText = wanted,
                        Binding = (JObject)binding.DeepClone(),
                        ExpectedAngles = JObject.FromObject(current.Angles),
                        ExpectedRotation = current.Rotation
                    });
                }
                RequireRefreshBudget(plan);
                // Read-only inspection has no Commit and no document write lock.
            }
            return plan;
        }

        private static void ApplyRefresh(RefreshContext context, RefreshPlan plan)
        {
            // The caller owns the document write lock. Reopen every object and
            // validate the whole plan before the first UpgradeOpen or setter.
            var db = context.Database;
            using (var tx = db.TransactionManager.StartOpenCloseTransaction())
            {
                var updates = new List<Tuple<AttributeReference, string, BlockReference>>();
                foreach (var change in plan.Updates)
                {
                    RequireRefreshBudget(plan);
                    BlockVariantService.Require(!change.ReferenceId.IsErased && !change.ReferenceId.IsEffectivelyErased,
                        "bound reference was erased before angle attribute refresh");
                    var reference = tx.GetObject(change.ReferenceId, OpenMode.ForRead) as BlockReference;
                    BlockVariantService.Require(reference != null && reference.IsDynamicBlock &&
                        reference.DynamicBlockTableRecord == change.DefinitionId, "bound reference definition changed");
                    var definition = (BlockTableRecord)tx.GetObject(change.DefinitionId, OpenMode.ForRead);
                    BlockVariantService.Require(!definition.IsDependent && !definition.IsFromExternalReference &&
                        JToken.DeepEquals(ReadBinding(tx, definition), change.Binding), "angle binding changed before refresh");
                    var attribute = Attribute(tx, reference, change.Tag);
                    BlockVariantService.Require(attribute != null && attribute.ObjectId == change.AttributeId &&
                        attribute.OwnerId == reference.ObjectId && !attribute.HasFields && !attribute.IsMTextAttribute &&
                        attribute.TextString == change.OldText, "bound attribute changed before refresh");
                    BlockVariantService.CheckLayer(tx, db, attribute.Layer);
                    BlockVariantService.CheckLayer(tx, db, reference.Layer);
                    var angles = BlockVariantService.ReadAngles(reference);
                    BlockVariantService.CheckAngles(angles, change.ExpectedAngles, 1e-9);
                    BlockVariantService.Require(Math.Abs(reference.Rotation-change.ExpectedRotation)<1e-9,
                        "block rotation changed before refresh");
                    BlockVariantService.Require(Calculate(tx, reference, angles, change.Binding,
                        () => Clock.ElapsedMilliseconds - plan.StartedAt < RefreshBudgetMilliseconds) == change.NewText,
                        "derived angle display changed before refresh");
                    updates.Add(Tuple.Create(attribute, change.NewText, reference));
                }
                RequireRefreshBudget(plan);
                // Real derived writes still need native grip/Undo/Redo verification.
                // Database Undo is never disabled; UpgradeOpen itself may start filing.
                // Attribute setters can synchronously notify their parent too.
                // Suppress every target before the first native write/open.
                foreach (var update in updates)
                {
                    context.OwnWrites.Add(update.Item1.ObjectId);
                    context.OwnWrites.Add(update.Item3.ObjectId);
                }
                foreach (var update in updates)
                {
                    var attribute = update.Item1;
                    attribute.UpgradeOpen();
                    attribute.DisableUndoRecording(true);
                    try { attribute.TextString = update.Item2; }
                    finally { attribute.DisableUndoRecording(false); }
                    BlockVariantService.Require(attribute.TextString == update.Item2,
                        "derived attribute write did not retain the native display text");
                }
                foreach (var parent in updates.Select(u => u.Item3).GroupBy(r => r.ObjectId).Select(g => g.First()))
                {
                    parent.UpgradeOpen();
                    parent.DisableUndoRecording(true);
                    try { parent.RecordGraphicsModified(true); }
                    finally { parent.DisableUndoRecording(false); }
                }
                tx.Commit();
            }
        }

        private static void RequireRefreshBudget(RefreshPlan plan) => BlockVariantService.Require(
            Clock.ElapsedMilliseconds - plan.StartedAt < RefreshBudgetMilliseconds, "angle refresh time budget exceeded");
    }
}
