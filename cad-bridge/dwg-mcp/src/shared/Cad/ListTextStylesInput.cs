using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Cad
{
    // Host-free validation shared by the typed MCP method and native handler.
    public sealed class ListTextStylesInput
    {
        public const int DefaultLimit = 100;
        public const int MaximumLimit = 1000;

        private ListTextStylesInput(int limit) { Limit = limit; }

        public int Limit { get; }

        public static bool TryParse(JToken parameters, out ListTextStylesInput input, out string error)
        {
            input = null;
            error = null;
            if (parameters != null && parameters.Type != JTokenType.Null && !(parameters is JObject))
            {
                error = "list_text_styles params must be an object";
                return false;
            }

            var token = (parameters as JObject)?["limit"];
            var limit = DefaultLimit;
            if (token != null && token.Type != JTokenType.Null)
            {
                // Reject floats/strings instead of relying on Newtonsoft numeric coercion.
                if (token.Type != JTokenType.Integer ||
                    !int.TryParse(token.ToString(), out limit) || limit < 1 || limit > MaximumLimit)
                {
                    error = "limit must be an integer between 1 and 1000";
                    return false;
                }
            }

            input = new ListTextStylesInput(limit);
            return true;
        }
    }
}
