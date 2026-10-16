using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
namespace Bimwright.Dwg.Plugin.Blocks
{
    public sealed class BlockAngleInput
    {
        public JObject Request {get;private set;}
        public string ExpectedDocument {get;private set;}
        public string ExpectedFingerprint {get;private set;}
        public bool DryRun {get;private set;}
        public static bool TryParse(JToken token,out BlockAngleInput input,out string error)
        {
            input=null;error=null;
            try
            {
                var o=BlockEditValidation.Object(token);
                BlockEditValidation.Keys(o,"expected_document","expected_fingerprint","handle","expected_name","expected_angles","new_angles","binding","expected_binding","dry_run");
                BlockEditValidation.Identity(o,out var document,out var fingerprint);
                BlockEditValidation.Handle(o["handle"]);BlockEditValidation.String(o,"expected_name",255,false);
                var expected=BlockEditValidation.Angles(o["expected_angles"],false);
                var changes=BlockEditValidation.Angles(o["new_angles"],true);
                foreach(var p in changes.Properties()) BlockEditValidation.Require(expected[p.Name]!=null,"new_angles property absent from expected_angles");
                if(o["binding"]!=null&&o["binding"].Type!=JTokenType.Null)
                {
                    var b=BlockEditValidation.Object(o["binding"]);
                    ValidateBinding(b,expected,true);
                }
                if(o["expected_binding"]!=null&&o["expected_binding"].Type!=JTokenType.Null)
                {
                    BlockEditValidation.Require(o["binding"] is JObject,"expected_binding requires a replacement binding");
                    var previous=BlockEditValidation.Object(o["expected_binding"]);
                    ValidateBinding(previous,expected,false);
                    BlockEditValidation.Require((string)previous["tag"]==(string)o["binding"]["tag"],"replacing a binding must retain its attribute tag");
                }
                BlockEditValidation.Require(changes.Count>0||o["binding"] is JObject,"provide new_angles or binding");
                input=new BlockAngleInput{Request=o,ExpectedDocument=document,ExpectedFingerprint=fingerprint,DryRun=o["dry_run"]==null?false:BlockEditValidation.Boolean(o["dry_run"])};return true;
            }
            catch(ArgumentException e){error=e.Message;return false;}
        }
        private static void ValidateBinding(JObject b,JObject expected,bool withText)
        {
            BlockEditValidation.Keys(b,withText?new[]{"tag","expected_text","primary_property","secondary_property","mode","precision"}:new[]{"tag","primary_property","secondary_property","mode","precision"});
            BlockEditValidation.String(b,"tag",255,false);
            if(withText)BlockEditValidation.String(b,"expected_text",8192,true);
            var mode=BlockEditValidation.String(b,"mode",32,false);
            if(mode=="dimension_display_degrees")
            {
                foreach(var key in new[]{"primary_property","secondary_property","precision"})
                    BlockEditValidation.Require(b[key]==null||b[key].Type==JTokenType.Null,"dimension_display_degrees uses native dimension formatting, not "+key);
                return;
            }
            BlockEditValidation.Require(mode=="property_degrees"||mode=="deflection_between_arms","unsupported binding mode");
            var primary=BlockEditValidation.String(b,"primary_property",255,false);
            BlockEditValidation.Require(expected[primary]!=null,"binding primary property absent from expected_angles");
            if(mode=="deflection_between_arms")
            {var secondary=BlockEditValidation.String(b,"secondary_property",255,false);BlockEditValidation.Require(secondary!=primary&&expected[secondary]!=null,"invalid secondary property");}
            else BlockEditValidation.Require(b["secondary_property"]==null||b["secondary_property"].Type==JTokenType.Null,"secondary_property only for deflection_between_arms");
            BlockEditValidation.Require(b["precision"]?.Type==JTokenType.Integer&&(int)b["precision"]>=0&&(int)b["precision"]<=6,"precision must be an integer 0..6");
        }
        public static string Format(double primary,double secondary,string mode,int precision)
        {
            if(double.IsNaN(primary)||double.IsInfinity(primary)||double.IsNaN(secondary)||double.IsInfinity(secondary)||precision<0||precision>6)throw new ArgumentException("invalid angle calculation");
            double degrees;
            if(mode=="property_degrees")degrees=primary*180/Math.PI;
            else if(mode=="deflection_between_arms")
            { var difference=Math.Abs(Math.IEEERemainder(primary-secondary,2*Math.PI));degrees=(Math.PI-difference)*180/Math.PI; }
            else throw new ArgumentException("unsupported angle mode");
            if(Math.Abs(degrees)<0.5*Math.Pow(10,-precision))degrees=0;
            return Math.Round(degrees,precision,MidpointRounding.AwayFromZero).ToString("0"+(precision==0?"":"."+new string('#',precision)),CultureInfo.InvariantCulture);
        }
    }
}
