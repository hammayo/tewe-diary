export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true" className={className}>
      <rect width="24" height="24" rx="4" fill="var(--color-accent)" />
      <path d="M6.5 7h11v2.6h-4.2V18h-2.6V9.6H6.5z" fill="#fff" />
    </svg>
  );
}
