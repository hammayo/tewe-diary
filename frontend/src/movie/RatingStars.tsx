import { Star } from "lucide-react";
import { Link } from "react-router";
import { useFocusNav } from "@/focus/FocusProvider";
import { cn } from "@/lib/utils";

interface Props {
  value: number | null;
  onChange: (rating: number) => void;
  onClear: () => void;
  /** Signed out: show a prompt to choose a profile instead of the stars. */
  disabled?: boolean;
  /** Focus-grid row of the stars (the page registers its rows, D10). */
  focusRow?: number;
}

const STARS = [1, 2, 3, 4, 5];

// A radio group: Tab lands on the current star, left and right change the rating (FR-8).
export function RatingStars({ value, onChange, onClear, disabled = false, focusRow }: Props) {
  const { register } = useFocusNav();

  if (disabled) {
    return (
      <p className="text-muted">
        <Link to="/profiles" className="underline underline-offset-4">
          Choose a profile
        </Link>{" "}
        to rate this film.
      </p>
    );
  }

  function onKeyDown(event: React.KeyboardEvent, n: number) {
    const target = event.key === "ArrowRight" ? n + 1 : event.key === "ArrowLeft" ? n - 1 : null;
    if (target === null) return;
    event.preventDefault();
    event.nativeEvent.stopPropagation(); // these arrows belong to the stars, not to the screen's focus grid
    if (target >= 1 && target <= 5) {
      onChange(target);
      (event.currentTarget.parentElement?.children[target - 1] as HTMLElement | undefined)?.focus();
    }
  }

  const tabbable = value ?? 1;
  return (
    <div className="flex items-center gap-6">
      <div role="radiogroup" aria-label="Your rating" className="flex gap-1 rounded-md bg-black/40 p-2">
        {STARS.map((n) => (
          <button
            key={n}
            type="button"
            role="radio"
            aria-checked={value === n}
            aria-label={`${n} ${n === 1 ? "star" : "stars"}`}
            tabIndex={n === tabbable ? 0 : -1}
            ref={(el) => {
              if (focusRow !== undefined && n === tabbable) register(focusRow, 0, el);
            }}
            onClick={() => onChange(n)}
            onKeyDown={(event) => onKeyDown(event, n)}
            className="focus-ring rounded-sm p-1"
          >
            <Star className={cn("size-7", value !== null && n <= value ? "fill-star text-star" : "text-star")} aria-hidden="true" />
          </button>
        ))}
      </div>
      {value !== null && (
        <button
          type="button"
          onClick={onClear}
          ref={(el) => {
            if (focusRow !== undefined) register(focusRow + 1, 0, el);
          }}
          className="focus-ring text-muted underline underline-offset-4 hover:text-white"
        >
          Remove my rating
        </button>
      )}
    </div>
  );
}
