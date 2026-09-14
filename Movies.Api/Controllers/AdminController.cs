using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Movies.Api.Auth;

namespace Movies.Api.Controllers;

[ApiController]
[ApiVersion(1.0)]
public class AdminController : ControllerBase
{
    private readonly IOutputCacheStore _outputCacheStore;

    public AdminController(IOutputCacheStore outputCacheStore)
    {
        _outputCacheStore = outputCacheStore;
    }

    // Evicts the "movies" output-cache tag. The TMDB import writes to the database directly,
    // bypassing the controllers' cache eviction, so the deploy pipeline calls this after a
    // successful import to drop stale movie list/detail responses. API-key protected so a
    // machine caller doesn't need a JWT.
    [ServiceFilter(typeof(ApiKeyAuthFilter))]
    [HttpPost(ApiEndpoints.Admin.EvictCache)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> EvictCache(CancellationToken token)
    {
        await _outputCacheStore.EvictByTagAsync("movies", token);
        return Ok();
    }
}
