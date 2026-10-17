namespace Movies.Application.Models;

// One person credited on a movie. Role is the character (cast) or the job (writer); null for directors.
public record MovieCredit(long TmdbPersonId, string Name, string? Role, string? ProfilePath);
