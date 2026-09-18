using System.Text;
using System.Text.Json;

namespace Movies.Api.Tests;

// Small JSON helpers so tests serialize requests and read responses the same way the API does
// (web defaults = camelCase, case-insensitive) without repeating serializer setup everywhere.
public static class HttpJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static StringContent AsJsonContent(this object value) =>
        new(JsonSerializer.Serialize(value, Options), Encoding.UTF8, "application/json");

    public static async Task<T?> ReadJsonAsync<T>(this HttpResponseMessage response) =>
        JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), Options);
}
