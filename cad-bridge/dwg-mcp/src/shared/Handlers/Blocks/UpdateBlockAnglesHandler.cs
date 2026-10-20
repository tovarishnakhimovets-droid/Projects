using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;
namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class UpdateBlockAnglesHandler:IAcadCommand
    {
        public string Name=>"update_block_angles";
        public string Description=>"Set explicitly guarded raw angular dynamic properties and optionally persist a derived angle-attribute binding; never save.";
        public CommandSchema Schema=>CommandSchemas.UpdateBlockAngles;
        public CommandResult Execute(Document doc,JToken parameters)
        {
            if(!BlockAngleInput.TryParse(parameters,out var input,out var error))return CommandResult.Fail(error);
            error=DocumentGuard.Validate(doc,input.ExpectedDocument,input.ExpectedFingerprint);if(error!=null)return CommandResult.Fail(error);
            try{return CommandResult.Success(BlockAngleBindingService.Change(doc,input));}
            catch(Exception e){return CommandResult.Fail("dynamic angle change failed: "+ErrorSanitizer.Sanitize(e.Message));}
        }
    }
}
