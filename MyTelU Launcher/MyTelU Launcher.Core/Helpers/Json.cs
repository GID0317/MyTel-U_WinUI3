#nullable enable

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MyTelU_Launcher.Core.Helpers;

public static class Json
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static Task<T?> ToObjectAsync<T>(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult<T?>(default);
        }

        return Task.FromResult(Deserialize<T>(value));
    }

    public static Task<string> StringifyAsync<T>(T value)
    {
        return Task.FromResult(Serialize(value));
    }

    public static T? Deserialize<T>(string value)
        => JsonSerializer.Deserialize(value, (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T)));

    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T)));
}
