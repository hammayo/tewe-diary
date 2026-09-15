using Movies.Tests.Shared;

namespace Movies.Application.Tests;

// Binds the "postgres" collection to the shared container fixture (Movies.Tests.Shared),
// so repository/import/migration tests reuse one migrated Postgres instance.
[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
