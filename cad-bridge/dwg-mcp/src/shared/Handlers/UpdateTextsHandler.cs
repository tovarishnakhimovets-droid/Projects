using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class UpdateTextsHandler : IAcadCommand
    {
        public string Name => "update_texts";
        public string Description => "Update text entities by handle.";
        public CommandSchema Schema => CommandSchemas.UpdateTexts;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            JArray items;
            if (parameters is JObject request &&
                (request.Property("apply_unicode_style") != null || request.Property("final_scale") != null))
                return CommandResult.Fail("automatic formatting moved to BWUNICODE; update_texts only changes text");
            if (!TryParseRequest(parameters, out items))
                return CommandResult.Fail("params must be { items: [...] }");

            var db = doc.Database;
            var results = new List<object>();

            using (var tx = db.TransactionManager.StartTransaction())
            {
                foreach (var item in items)
                {
                    var handleStr = (string)item["handle"];
                    var newText = (string)item["new_text"];

                    if (string.IsNullOrWhiteSpace(handleStr))
                    {
                        results.Add(new { handle = handleStr, ok = false, error = "empty handle" });
                        continue;
                    }

                    long handleLong;
                    if (!long.TryParse(handleStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out handleLong))
                    {
                        results.Add(new { handle = handleStr, ok = false, error = "invalid handle format" });
                        continue;
                    }

                    ObjectId id;
                    try
                    {
                        id = db.GetObjectId(false, new Handle(handleLong), 0);
                    }
                    catch (Exception)
                    {
                        results.Add(new { handle = handleStr, ok = false, error = "handle not found" });
                        continue;
                    }

                    try
                    {
                        var obj = tx.GetObject(id, OpenMode.ForWrite);
                        switch (obj)
                        {
                            case AttributeReference att:
                                att.TextString = newText ?? string.Empty;
                                break;
                            case DBText dbText:
                                dbText.TextString = newText ?? string.Empty;
                                break;
                            case MText mText:
                                mText.Contents = newText ?? string.Empty;
                                break;
                            case MLeader mLeader:
                                using (var mt = mLeader.MText)
                                {
                                    if (mt == null)
                                    {
                                        results.Add(new { handle = handleStr, ok = false, error = "mleader has no mtext" });
                                        continue;
                                    }
                                    mt.Contents = newText ?? string.Empty;
                                    mLeader.MText = mt;
                                }
                                break;
                            default:
                                results.Add(new { handle = handleStr, ok = false, error = $"not a text entity: {obj.GetType().Name}" });
                                continue;
                        }
                        results.Add(new { handle = handleStr, ok = true, error = (string)null });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { handle = handleStr, ok = false, error = ex.Message });
                    }
                }
                tx.Commit();
            }

            return CommandResult.Success(results);
        }

        private static bool TryParseRequest(JToken parameters, out JArray items)
        {
            items = null;

            if (parameters == null) return false;

            if (parameters.Type == JTokenType.Array)
            {
                items = (JArray)parameters;
                return true;
            }

            if (parameters.Type != JTokenType.Object) return false;

            items = parameters["items"] as JArray;
            if (items == null) return false;

            return true;
        }
    }
}
