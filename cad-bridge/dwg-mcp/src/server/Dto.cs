using Newtonsoft.Json;

namespace Bimwright.Dwg.Server
{
    public class McpRequest
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("cmd")] public string Cmd { get; set; }
        [JsonProperty("params")] public object Params { get; set; }
        [JsonProperty("auth")] public string Auth { get; set; }
        [JsonProperty("document_id", NullValueHandling = NullValueHandling.Ignore)] public string DocumentId { get; set; }
    }

    public class McpResponse
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("ok")] public bool Ok { get; set; }
        [JsonProperty("result")] public object Result { get; set; }
        [JsonProperty("error")] public string Error { get; set; }
    }

    public class UpdateTextItem
    {
        [JsonProperty("handle")] public string Handle { get; set; }
        [JsonProperty("new_text")] public string NewText { get; set; }
    }

    public class UpdateTextsRequest
    {
        [JsonProperty("items")] public UpdateTextItem[] Items { get; set; }
    }
}
