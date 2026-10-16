using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;
namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class CreateBlockVariantHandler : IAcadCommand
    {
        public string Name=>"create_block_variant";
        public string Description=>"Clone one guarded dynamic definition and make bounded geometry/default edits in its independent variant; never save.";
        public CommandSchema Schema=>CommandSchemas.CreateBlockVariant;
        public CommandResult Execute(Document doc,JToken parameters)
        {
            if(!BlockVariantInput.TryParse(parameters,out var input,out var error))return CommandResult.Fail(error);
            error=DocumentGuard.Validate(doc,input.ExpectedDocument,input.ExpectedFingerprint);if(error!=null)return CommandResult.Fail(error);
            try{return CommandResult.Success(BlockVariantService.Create(doc,input));}
            catch(Exception e){return CommandResult.Fail("block variant failed: "+ErrorSanitizer.Sanitize(e.Message));}
        }
    }
}
