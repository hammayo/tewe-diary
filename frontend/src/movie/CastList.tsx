import type { Credit } from "@/api/types";

function initials(name: string): string {
  return name
    .split(/\s+/)
    .map((part) => part[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();
}

export function CastList({ cast }: { cast: Credit[] }) {
  if (cast.length === 0) return null;
  return (
    <section aria-label="Cast">
      <h2 className="mb-4 font-display text-2xl font-bold">Cast</h2>
      <ul className="flex flex-wrap gap-x-8 gap-y-6">
        {cast.map((person) => (
          <li key={`${person.tmdbPersonId}-${person.role}`} className="w-32 text-center">
            {person.profileUrl ? (
              <img src={person.profileUrl} alt="" loading="lazy" className="mx-auto size-24 rounded-full bg-surface object-cover" />
            ) : (
              <span className="mx-auto grid size-24 place-items-center rounded-full bg-surface-raised text-xl font-semibold text-muted" aria-hidden="true">
                {initials(person.name)}
              </span>
            )}
            <p className="mt-2 text-sm font-semibold">{person.name}</p>
            {person.role && <p className="text-xs text-muted">{person.role}</p>}
          </li>
        ))}
      </ul>
    </section>
  );
}
