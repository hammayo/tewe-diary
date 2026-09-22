namespace Movies.Application.Database.Import;

// A progress report from MovieImporter: lines staged so far; Transforming once staging has finished
// and the upsert transform is running; Notice carries a server NOTICE raised by the transform
// (e.g. "Import: 7 inserted, 3 refreshed, 0 skipped (slug collision)").
public sealed record ImportProgress(int Staged, bool Transforming, string? Notice = null);
