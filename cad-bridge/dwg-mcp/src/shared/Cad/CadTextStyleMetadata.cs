using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.Cad
{
    internal static class CadTextStyleMetadata
    {
        // Only copied scalars leave the transaction; never serialize an Autodesk object.
        internal static Dictionary<string, object> Describe(TextStyleTableRecord style)
        {
            if (style == null) throw new ArgumentNullException(nameof(style));
            var result = new Dictionary<string, object>
            {
                ["name"] = style.Name,
                ["handle"] = style.Handle.ToString(),
                ["font_file"] = style.FileName,
                ["big_font_file"] = style.BigFontFileName,
                ["text_size"] = style.TextSize,
                ["width_factor"] = style.XScale,
                ["oblique"] = style.ObliquingAngle,
                ["is_vertical"] = style.IsVertical,
                ["is_shape_file"] = style.IsShapeFile,
                ["is_dependent"] = style.IsDependent,
                ["typeface"] = null,
                ["bold"] = null,
                ["italic"] = null,
                ["character_set"] = null,
                ["pitch_and_family"] = null,
                ["font_metadata_error"] = null
            };
            try
            {
                var font = style.Font;
                result["typeface"] = font.TypeFace;
                result["bold"] = font.Bold;
                result["italic"] = font.Italic;
                result["character_set"] = font.CharacterSet;
                result["pitch_and_family"] = font.PitchAndFamily;
            }
            catch (Exception ex)
            {
                // A missing/unavailable font descriptor must not hide the stored SHX file.
                result["font_metadata_error"] = ex.Message;
            }
            return result;
        }

        internal static void AddToEntity(IDictionary<string, object> result, ObjectId styleId, Transaction tx)
        {
            result["style_name"] = null;
            result["style_handle"] = null;
            result["font_file"] = null;
            result["big_font_file"] = null;
            result["typeface"] = null;
            result["bold"] = null;
            result["italic"] = null;
            result["character_set"] = null;
            result["pitch_and_family"] = null;
            result["font_metadata_error"] = null;
            result["style_width_factor"] = null;
            result["style_oblique"] = null;
            result["style_text_size"] = null;
            result["style_is_vertical"] = null;
            result["style_metadata_error"] = null;
            if (tx == null || styleId.IsNull)
            {
                result["style_metadata_error"] = "text style is unavailable";
                return;
            }

            try
            {
                var style = tx.GetObject(styleId, OpenMode.ForRead) as TextStyleTableRecord;
                if (style == null)
                {
                    result["style_metadata_error"] = "referenced object is not a text style";
                    return;
                }
                var metadata = Describe(style);
                result["style_name"] = metadata["name"];
                result["style_handle"] = metadata["handle"];
                foreach (var name in new[] { "font_file", "big_font_file", "typeface", "bold", "italic",
                    "character_set", "pitch_and_family", "font_metadata_error" })
                    result[name] = metadata[name];
                // Style defaults are distinct from existing DBText's own width/oblique.
                result["style_width_factor"] = metadata["width_factor"];
                result["style_oblique"] = metadata["oblique"];
                result["style_text_size"] = metadata["text_size"];
                result["style_is_vertical"] = metadata["is_vertical"];
            }
            catch (Exception ex)
            {
                result["style_metadata_error"] = ex.Message;
            }
        }
    }
}
