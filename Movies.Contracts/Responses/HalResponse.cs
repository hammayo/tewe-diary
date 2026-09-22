using System.Text.Json.Serialization;

namespace Movies.Contracts.Responses;

// Base for responses that carry HAL-style links (e.g. a movie's own URL). Omitted from the JSON
// when there are none.
public abstract class HalResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Link>? Links { get; set; }
}

public class Link
{
    public required string Href { get; set; }

    public required string Rel { get; set; }

    // HTTP method to use on Href, e.g. "GET".
    public required string Type { get; set; }
}
