using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
                tx.Commit();Initialize();Documents[doc].Notify(DeferredAngleRefreshNotification.ExternalChange);return result;
            }
        }
        private static string Calculate(Transaction tx,BlockReference reference,Dictionary<string,double> angles,JObject b,
            Func<bool> continueInspection=null,Action<JObject> nativeReadback=null)
        {
            if((string)b["mode"]=="dimension_display_degrees")
            {
                var native=BlockAngularDimensionReader.ReadCurrent(tx,reference,500,continueInspection);
                BlockVariantService.Require(native.Value<bool>("complete")&&native.Value<int>("matching_count")==1&&native["unique_numeric_text"]?.Type==JTokenType.String,"evaluated angular dimension is missing, ambiguous or unsupported: "+(string)native["error"]);
                nativeReadback?.Invoke(native);
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
        private static RefreshReadback Refresh(RefreshContext context)
        {
            var db=context.Database;var watch=Stopwatch.StartNew();
            using(var tx=db.TransactionManager.StartTransaction())
            {
                var table=(BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead);
                var updates=new List<Tuple<AttributeReference,string,BlockReference>>();
                var references=new HashSet<ObjectId>();var evaluated=new HashSet<ObjectId>();
                var snapshot=new JArray();int definitions=0,snapshotCharacters=0;
                foreach(ObjectId id in table)
                {
                    BlockVariantService.Require(watch.ElapsedMilliseconds<RefreshBudgetMilliseconds,"angle refresh time budget exceeded");
                    BlockVariantService.Require(++definitions<=10000,"angle refresh definition limit exceeded");
                    var definition=(BlockTableRecord)tx.GetObject(id,OpenMode.ForRead);
                    if(definition.IsAnonymous||definition.IsDependent||definition.IsFromExternalReference)continue;
                    var binding=ReadBinding(tx,definition);if(binding==null)continue;
                    var records=new List<BlockTableRecord>{definition};
                    foreach(ObjectId anonymous in definition.GetAnonymousBlockIds())records.Add((BlockTableRecord)tx.GetObject(anonymous,OpenMode.ForRead));
                    foreach(var record in records)foreach(ObjectId refId in record.GetBlockReferenceIds(true,false))
                    {
                        if(!references.Add(refId)||refId.IsErased||refId.IsEffectivelyErased)continue;
                        BlockVariantService.Require(references.Count<=1000,"angle refresh reference limit exceeded");
                        var reference=tx.GetObject(refId,OpenMode.ForRead) as BlockReference;
                        if(reference==null||!reference.IsDynamicBlock)continue;
                        var a=Attribute(tx,reference,(string)binding["tag"]);
                        BlockVariantService.Require(a!=null&&!a.HasFields&&!a.IsMTextAttribute,"bound attribute missing or contains a field");
                        var angles=BlockVariantService.ReadAngles(reference);JObject native=null;
                        var wanted=Calculate(tx,reference,angles,binding,
                            ()=>watch.ElapsedMilliseconds<RefreshBudgetMilliseconds,n=>native=n);
                        var source=new JObject{["reference"]=reference.Handle.ToString(),
                            ["evaluated_definition"]=reference.BlockTableRecord.Handle.ToString(),
                            ["angles"]=new JObject(angles.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new JProperty(p.Key,p.Value))),
                            ["attribute_text"]=wanted};
                        if(native!=null)
                        {
                            var dimension=(JObject)native["dimensions"][0];
                            source["dimension"]=dimension["handle"].DeepClone();
                            source["measurement_radians"]=dimension["measurement_radians"].DeepClone();
                            source["formatted"]=dimension["formatted_plain_text"].DeepClone();
                            evaluated.Add(reference.BlockTableRecord);
                        }
                        snapshotCharacters+=source.ToString(Formatting.None).Length;
                        BlockVariantService.Require(snapshotCharacters<=256*1024,"angle refresh snapshot limit exceeded");
                        snapshot.Add(source);
                        if(a.TextString==wanted)continue;
                        BlockVariantService.CheckLayer(tx,db,a.Layer);BlockVariantService.CheckLayer(tx,db,reference.Layer);
                        updates.Add(Tuple.Create(a,wanted,reference));
                    }
                }
                BlockVariantService.Require(watch.ElapsedMilliseconds<RefreshBudgetMilliseconds,"angle refresh time budget exceeded");
                // Native command/Undo integration remains to be verified. UpgradeOpen itself can
                // begin Undo filing; do not claim this is one-Undo proof. Database Undo is NEVER disabled.
                foreach(var update in updates)
                {
                    var a=update.Item1;context.OwnWrites.Add(a.ObjectId);a.UpgradeOpen();a.DisableUndoRecording(true);
                    try{a.TextString=update.Item2;}finally{a.DisableUndoRecording(false);}
                    BlockVariantService.Require(a.TextString==update.Item2,"derived attribute write did not retain the native display text");
                }
                foreach(var parent in updates.Select(u=>u.Item3).GroupBy(r=>r.ObjectId).Select(g=>g.First()))
                {
                    context.OwnWrites.Add(parent.ObjectId);parent.UpgradeOpen();parent.DisableUndoRecording(true);
                    try{parent.RecordGraphicsModified(true);}finally{parent.DisableUndoRecording(false);}
                }
                tx.Commit();
                context.BoundReferences=references;context.EvaluatedDefinitions=evaluated;
                // The policy retains a bounded signature, not a growing copy of block data.
                // Include raw source values even when native rounding leaves display text unchanged.
                using(var hash=SHA256.Create())
                    return new RefreshReadback{Snapshot=Convert.ToBase64String(hash.ComputeHash(
                        Encoding.UTF8.GetBytes(snapshot.ToString(Formatting.None)))),Wrote=updates.Count!=0};
            }
        }
    }
}
