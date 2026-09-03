import { useEffect, useState } from "react";
import { deleteJob, listJobs } from "../api/interviews";
import type { TranscriptionJobSummary } from "../types/job";

interface Props {
  onSelect: (jobId: string) => void;
  onClose: () => void;
}

const PAGE_SIZE = 20;

const STATUS_LABELS: Record<string, string> = {
  Pending: "Kuyrukta",
  Processing: "İşleniyor",
  Completed: "Tamamlandı",
  Failed: "Hata",
  Cancelled: "İptal Edildi",
};

const STATUS_COLORS: Record<string, string> = {
  Pending: "text-stamp-pending",
  Processing: "text-stamp-pending",
  Completed: "text-stamp-completed",
  Failed: "text-stamp-failed",
  Cancelled: "text-paper-muted",
};

function formatDate(iso: string): string {
  const date = new Date(iso);
  return date.toLocaleString("tr-TR", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function formatDuration(seconds: number | null): string {
  if (seconds === null) return "—";
  const minutes = Math.floor(seconds / 60);
  const secs = Math.round(seconds % 60);
  return `${minutes}dk ${secs}sn`;
}

// 2 aşamalı silme onayı: "idle" -> (sil ikonuna tık) -> "confirm1" ->
// (Evet, Sil'e tık) -> "confirm2" -> (Kesinlikle Sil'e tık) -> gerçek silme.
// Toplamda 3 tıklama gerekiyor, yanlışlıkla tek tıkla veri kaybını önlemek için.
type DeleteStage = "idle" | "confirm1" | "confirm2";

export function JobHistoryList({ onSelect, onClose }: Props) {
  const [jobs, setJobs] = useState<TranscriptionJobSummary[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [deleteStage, setDeleteStage] = useState<Record<string, DeleteStage>>({});
  const [deletingId, setDeletingId] = useState<string | null>(null);

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  const loadPage = (targetPage: number) => {
    setLoading(true);
    setError(null);
    listJobs(targetPage, PAGE_SIZE)
      .then((result) => {
        setJobs(result.items);
        setTotalCount(result.totalCount);
        setPage(result.page);
      })
      .catch((err) => setError(err instanceof Error ? err.message : "Liste alınamadı."))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    loadPage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const normalizedQuery = query.trim().toLocaleLowerCase("tr-TR");
  const filteredJobs = normalizedQuery
    ? jobs.filter((job) => {
        const name = (job.originalFileName ?? "").toLocaleLowerCase("tr-TR");
        const idShort = job.id.slice(0, 8).toLocaleLowerCase("tr-TR");
        return name.includes(normalizedQuery) || idShort.includes(normalizedQuery);
      })
    : jobs;

  const handleDeleteClick = (jobId: string) => {
    setDeleteStage((prev) => {
      const current = prev[jobId] ?? "idle";
      const next: DeleteStage = current === "idle" ? "confirm1" : "confirm2";
      return { ...prev, [jobId]: next };
    });
  };

  const handleFinalDelete = async (jobId: string) => {
    setDeletingId(jobId);
    try {
      await deleteJob(jobId);
      setJobs((prev) => prev.filter((j) => j.id !== jobId));
      setTotalCount((prev) => Math.max(0, prev - 1));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Silinirken hata oluştu.");
    } finally {
      setDeletingId(null);
      setDeleteStage((prev) => {
        const { [jobId]: _removed, ...rest } = prev;
        return rest;
      });
    }
  };

  const handleCancelDelete = (jobId: string) => {
    setDeleteStage((prev) => {
      const { [jobId]: _removed, ...rest } = prev;
      return rest;
    });
  };

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="font-stamp text-xs uppercase tracking-[0.2em] text-paper-muted">
          Geçmiş İşlemler {totalCount > 0 && `(${totalCount})`}
        </h2>
        <button
          type="button"
          onClick={onClose}
          className="font-stamp text-[10px] uppercase tracking-[0.15em] text-paper-muted hover:text-paper"
        >
          Kapat ✕
        </button>
      </div>

      {!loading && !error && jobs.length > 0 && (
        <div className="space-y-1">
          <input
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Dosya adı veya dosya no ile ara…"
            className="w-full rounded-sm border border-ink-border bg-ink-panelAlt px-3 py-2 font-body text-sm text-paper placeholder:text-paper-muted focus:border-paper-muted focus:outline-none"
          />
          {normalizedQuery && (
            <p className="font-body text-[11px] text-paper-muted">
              Arama sadece bu sayfadaki ({PAGE_SIZE}) kayıt içinde çalışır.
            </p>
          )}
        </div>
      )}

      {loading && <p className="font-body text-sm text-paper-muted">Yükleniyor…</p>}

      {error && (
        <p className="rounded-sm border border-stamp-failed/40 bg-stamp-failed/10 px-3 py-2 font-body text-sm text-stamp-failed">
          {error}
        </p>
      )}

      {!loading && !error && jobs.length === 0 && (
        <p className="font-body text-sm text-paper-muted">Henüz hiç işlem yapılmamış.</p>
      )}

      {!loading && !error && jobs.length > 0 && filteredJobs.length === 0 && (
        <p className="font-body text-sm text-paper-muted">"{query}" ile eşleşen bir kayıt yok.</p>
      )}

      <div className="max-h-80 space-y-1.5 overflow-y-auto">
        {filteredJobs.map((job) => {
          const stage = deleteStage[job.id] ?? "idle";
          const isDeleting = deletingId === job.id;

          return (
            <div
              key={job.id}
              className="rounded-sm border border-ink-border bg-ink-panelAlt px-3 py-2.5"
            >
              <div className="flex items-center justify-between gap-2">
                <button
                  type="button"
                  onClick={() => onSelect(job.id)}
                  disabled={job.status !== "Completed"}
                  className="min-w-0 flex-1 text-left disabled:cursor-not-allowed disabled:opacity-50"
                >
                  <p className="truncate font-body text-sm text-paper">
                    {job.originalFileName ?? `Dosya No. ${job.id.slice(0, 8).toUpperCase()}`}
                  </p>
                  <p className="font-stamp text-[10px] uppercase tracking-[0.1em] text-paper-muted">
                    {formatDate(job.createdAtUtc)} · {formatDuration(job.audioDurationSeconds)}
                    {job.diarization && " · Konuşmacı Ayrımlı"}
                  </p>
                </button>

                <span
                  className={`shrink-0 font-stamp text-[10px] font-semibold uppercase tracking-[0.1em] ${STATUS_COLORS[job.status] ?? "text-paper-muted"}`}
                >
                  {STATUS_LABELS[job.status] ?? job.status}
                </span>

                {stage === "idle" && (
                  <button
                    type="button"
                    onClick={() => handleDeleteClick(job.id)}
                    title="Sil"
                    className="shrink-0 rounded-sm p-1 text-paper-muted transition-colors hover:text-stamp-failed"
                  >
                    🗑
                  </button>
                )}
              </div>

              {stage === "confirm1" && (
                <div className="mt-2 flex items-center justify-between gap-2 rounded-sm border border-stamp-failed/40 bg-stamp-failed/10 px-2.5 py-2">
                  <p className="font-body text-xs text-stamp-failed">Bu kayıt silinsin mi?</p>
                  <div className="flex shrink-0 gap-2">
                    <button
                      type="button"
                      onClick={() => handleCancelDelete(job.id)}
                      className="font-stamp text-[10px] uppercase tracking-[0.1em] text-paper-muted hover:text-paper"
                    >
                      Vazgeç
                    </button>
                    <button
                      type="button"
                      onClick={() => handleDeleteClick(job.id)}
                      className="font-stamp text-[10px] font-semibold uppercase tracking-[0.1em] text-stamp-failed underline"
                    >
                      Evet, Sil
                    </button>
                  </div>
                </div>
              )}

              {stage === "confirm2" && (
                <div className="mt-2 flex items-center justify-between gap-2 rounded-sm border border-stamp-failed bg-stamp-failed/20 px-2.5 py-2">
                  <p className="font-body text-xs font-semibold text-stamp-failed">
                    Son kez soruyoruz — GERİ ALINAMAZ.
                  </p>
                  <div className="flex shrink-0 gap-2">
                    <button
                      type="button"
                      onClick={() => handleCancelDelete(job.id)}
                      disabled={isDeleting}
                      className="font-stamp text-[10px] uppercase tracking-[0.1em] text-paper-muted hover:text-paper disabled:opacity-50"
                    >
                      Vazgeç
                    </button>
                    <button
                      type="button"
                      onClick={() => handleFinalDelete(job.id)}
                      disabled={isDeleting}
                      className="font-stamp text-[10px] font-semibold uppercase tracking-[0.1em] text-stamp-failed underline disabled:opacity-50"
                    >
                      {isDeleting ? "Siliniyor…" : "Kesinlikle Sil"}
                    </button>
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>

      {!loading && !error && !normalizedQuery && totalPages > 1 && (
        <div className="flex items-center justify-between pt-1">
          <button
            type="button"
            onClick={() => loadPage(page - 1)}
            disabled={page <= 1}
            className="font-stamp text-[10px] uppercase tracking-[0.1em] text-paper-muted hover:text-paper disabled:cursor-not-allowed disabled:opacity-30"
          >
            ← Önceki
          </button>
          <span className="font-stamp text-[10px] uppercase tracking-[0.1em] text-paper-muted">
            Sayfa {page} / {totalPages}
          </span>
          <button
            type="button"
            onClick={() => loadPage(page + 1)}
            disabled={page >= totalPages}
            className="font-stamp text-[10px] uppercase tracking-[0.1em] text-paper-muted hover:text-paper disabled:cursor-not-allowed disabled:opacity-30"
          >
            Sonraki →
          </button>
        </div>
      )}
    </div>
  );
}
