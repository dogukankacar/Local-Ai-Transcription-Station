import type { JobStatus } from "../types/job";

type StampState = "upcoming" | "active" | "done" | "failed";

const STAGES = [
  { label: "KUYRUKTA" },
  { label: "İŞLENİYOR" },
  { label: "TAMAMLANDI" },
] as const;

function computeStates(status: JobStatus | null): StampState[] {
  if (status === null) return ["upcoming", "upcoming", "upcoming"];
  if (status === "Pending") return ["active", "upcoming", "upcoming"];
  if (status === "Processing") return ["done", "active", "upcoming"];
  if (status === "Completed") return ["done", "done", "done"];
  // Backend her zaman Processing'e girdikten sonra Failed'e geçer.
  return ["done", "done", "failed"];
}

const STATE_STYLES: Record<StampState, string> = {
  upcoming: "border-ink-border text-paper-muted/50",
  active: "border-stamp-pending text-stamp-pending animate-pulse-slow",
  done: "border-stamp-completed text-stamp-completed animate-stamp-in",
  failed: "border-stamp-failed text-stamp-failed animate-stamp-in",
};

interface Props {
  status: JobStatus | null;
  errorMessage?: string | null;
}

export function StageStamps({ status, errorMessage }: Props) {
  const states = computeStates(status);

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        {STAGES.map((stage, i) => (
          <div key={stage.label} className="flex flex-1 items-center">
            <div className="flex flex-1 flex-col items-center gap-2">
              <div
                className={`flex h-16 w-16 -rotate-3 items-center justify-center rounded-full border-[3px] font-stamp text-[10px] font-bold uppercase leading-tight ${STATE_STYLES[states[i]]}`}
              >
                {states[i] === "failed" ? "HATA" : stage.label.slice(0, 4)}
              </div>
              <span className="font-stamp text-[10px] uppercase tracking-[0.15em] text-paper-muted">
                {states[i] === "failed" ? "HATA" : stage.label}
              </span>
            </div>
            {i < STAGES.length - 1 && (
              <div
                className={`mx-1 h-[2px] flex-1 ${states[i] === "done" ? "bg-stamp-completed/70" : "bg-ink-border"}`}
              />
            )}
          </div>
        ))}
      </div>

      {status === "Failed" && errorMessage && (
        <p className="rounded-sm border border-stamp-failed/40 bg-stamp-failed/10 px-3 py-2 font-body text-sm text-stamp-failed">
          {errorMessage}
        </p>
      )}
    </div>
  );
}
