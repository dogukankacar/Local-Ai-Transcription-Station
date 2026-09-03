interface Props {
  percent: number | null;
  message: string | null;
}

export function ProgressBar({ percent, message }: Props) {
  if (percent === null) return null;

  const clamped = Math.max(0, Math.min(100, percent));

  return (
    <div className="space-y-1.5">
      <div className="h-2 w-full overflow-hidden rounded-full bg-ink-border">
        <div
          className="h-full rounded-full bg-stamp-pending transition-[width] duration-500 ease-out"
          style={{ width: `${clamped}%` }}
        />
      </div>
      <div className="flex items-center justify-between font-stamp text-[10px] uppercase tracking-[0.1em] text-paper-muted">
        <span>{message ?? "İşleniyor"}</span>
        <span>%{clamped}</span>
      </div>
    </div>
  );
}
