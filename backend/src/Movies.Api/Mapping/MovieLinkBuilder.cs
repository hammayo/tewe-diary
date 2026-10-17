using Asp.Versioning;
using Movies.Contracts.Responses;

namespace Movies.Api.Mapping;

// Builds the HAL links on movie responses. Hrefs come from LinkGenerator, so they follow the real
// route (including the API version segment) instead of a hard-coded path, and are absolute so a
// client can follow them as-is.
public class MovieLinkBuilder
{
    private readonly LinkGenerator _linkGenerator;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MovieLinkBuilder(LinkGenerator linkGenerator, IHttpContextAccessor httpContextAccessor)
    {
        _linkGenerator = linkGenerator;
        _httpContextAccessor = httpContextAccessor;
    }

    // "self": GET the movie by slug. Null outside a request, or if the route can't be resolved, so a
    // response is never given a broken link.
    public Link? Self(string idOrSlug)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        var version = httpContext.GetRequestedApiVersion()?.ToString() ?? "1.0";
        var href = _linkGenerator.GetUriByAction(httpContext,
            action: "GetV1", controller: "Movies",
            values: new { idOrSlug, version });

        return href is null ? null : new Link { Href = href, Rel = "self", Type = "GET" };
    }
}
