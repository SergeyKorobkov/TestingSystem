using System.Text;
using System.Text.Json;

namespace Shared;

public static class Protocol
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Pack(string type, object payload)
        => JsonSerializer.Serialize(new { type, payload }, JsonOpts);

    public static (string Type, JsonElement Payload) Unpack(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        var type = root.GetProperty("type").GetString() ?? "";
        var payload = root.GetProperty("payload");
        return (type, payload.Clone());
    }

    public static T ReadPayload<T>((string Type, JsonElement Payload) env)
        => env.Payload.Deserialize<T>(JsonOpts)!;

    public static JsonSerializerOptions Options => JsonOpts;
}
