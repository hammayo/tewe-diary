namespace Movies.Api;

public static class ApiEndpoints
{
    // The v{version:apiVersion} segment is substituted from each controller's [ApiVersion]
    // (v1 today), so routing is version-aware and a v2 (e.g. minimal API) can live alongside.
    private const string ApiBase = "api/v{version:apiVersion}";
    
    public static class Movies
    {
        private const string Base = $"{ApiBase}/movies";

        public const string Create = Base;
        public const string Get = $"{Base}/{{idOrSlug}}";
        public const string GetAll = Base;
        public const string Update = $"{Base}/{{id:guid}}";
        public const string Delete = $"{Base}/{{id:guid}}";

        public const string Rate = $"{Base}/{{id:guid}}/ratings";
        public const string DeleteRating = $"{Base}/{{id:guid}}/ratings";
    }
    
    public static class Ratings
    {
        private const string Base = $"{ApiBase}/ratings";

        public const string GetUserRatings = $"{Base}/me";
    }

    public static class Admin
    {
        private const string Base = $"{ApiBase}/admin";

        public const string EvictCache = $"{Base}/cache/evict";
    }
}
