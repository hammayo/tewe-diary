using System.Diagnostics;
using Movies.Application.Database.Import;

namespace Movies.DbTool;

// Console output for MovieImporter progress, matching scripts/load-movies.sh. Staging redraws one line
// in place on a terminal, or prints every 10% when output is redirected (e.g. CI logs). Then it
// announces the transform, prints its NOTICE summary, and how long the transform took.
internal sealed class ConsoleImportProgress(int total, Stopwatch clock) : IProgress<ImportProgress>
{
    private readonly bool _interactive = !Console.IsOutputRedirected;
    private int _lastDecile = -1;
    private TimeSpan? _transformStarted;

    public void Report(ImportProgress value)
    {
        if (value.Notice is not null)
        {
            var took = clock.Elapsed - (_transformStarted ?? clock.Elapsed);
            Console.WriteLine($"  {value.Notice} (transform {Format(took)})");
            return;
        }

        if (value.Transforming)
        {
            if (_transformStarted is not null)
            {
                return;
            }

            _transformStarted = clock.Elapsed;
            if (_interactive)
            {
                Console.WriteLine();
            }

            Console.WriteLine("[2/3] Applying the upsert transform in one transaction (movies, genres, metadata, details, credits)...");
            return;
        }

        var percent = total == 0 ? 100 : value.Staged * 100 / total;
        var eta = value.Staged == 0 ? "--"
            : Format(TimeSpan.FromTicks(clock.Elapsed.Ticks * (total - value.Staged) / value.Staged));
        var line = $"  {value.Staged}/{total} ({percent}%) · {Format(clock.Elapsed)} elapsed · ETA {eta}";
        if (_interactive)
        {
            Console.Write($"\r{line}   ");
        }
        else if (percent / 10 > _lastDecile)
        {
            _lastDecile = percent / 10;
            Console.WriteLine(line);
        }
    }

    // 75s -> "1m15s", 3700s -> "1h01m40s" (same format as scripts/helpers/progress.sh).
    public static string Format(TimeSpan t)
    {
        if (t.TotalHours >= 1)
        {
            return $"{(int)t.TotalHours}h{t.Minutes:00}m{t.Seconds:00}s";
        }

        return t.TotalMinutes >= 1 ? $"{t.Minutes}m{t.Seconds:00}s" : $"{t.Seconds}s";
    }
}
