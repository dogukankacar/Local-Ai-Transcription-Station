import { useState } from "react";
import { UploadDropzone } from "./components/UploadDropzone";
import { StageStamps } from "./components/StageStamps";
import { ProgressBar } from "./components/ProgressBar";
import { RedactedTranscript } from "./components/RedactedTranscript";
import { JobHistoryList } from "./components/JobHistoryList";
import { useTranscriptionJob } from "./hooks/useTranscriptionJob";
import { downloadTextAsDocx } from "./utils/exportDocx";
import { downloadTextAsTxt } from "./utils/exportTxt";
import { downloadTextAsXlsx } from "./utils/exportXlsx";

export default function App() {
  const { state, job, error, start, cancel, reset, viewHistoricalJob } = useTranscriptionJob();
  const isBusy = state === "uploading" || state === "polling" || state === "cancelling";
  const [historyOpen, setHistoryOpen] = useState(false);

  return (
    <div className="flex min-h-screen items-center justify-center px-4 py-10">
      <div className="w-full max-w-xl rounded-sm border border-ink-border bg-ink-panel shadow-2xl shadow-black/40">
        {/* Başlık -- dosya klasörü sekmesi gibi */}
        <header className="flex items-center justify-between border-b border-ink-border px-6 py-4">
          <div>
            <h1 className="font-stamp text-sm font-bold uppercase tracking-[0.25em] text-paper">
              Lokal Deşifre İstasyonu
            </h1>
            <p className="mt-1 font-body text-xs text-paper-muted">
              Nitel araştırma verisi · KVKK / Etik Kurul uyumlu
            </p>
          </div>
          <span className="flex shrink-0 items-center gap-3">
            <button
              type="button"
              onClick={() => setHistoryOpen((v) => !v)}
              className="font-stamp text-[10px] uppercase tracking-[0.15em] text-paper-muted underline decoration-dotted underline-offset-4 hover:text-paper"
            >
              📁 Geçmiş
            </button>
            <span className="flex items-center gap-2 rounded-full border border-stamp-completed/40 px-3 py-1 font-stamp text-[10px] font-semibold uppercase tracking-[0.15em] text-stamp-completed">
              <span className="h-1.5 w-1.5 rounded-full bg-stamp-completed" />
              Yerel · Çevrimdışı
            </span>
          </span>
        </header>

        <main className="space-y-8 px-6 py-6">
          {historyOpen && (
            <JobHistoryList
              onSelect={(jobId) => {
                setHistoryOpen(false);
                viewHistoricalJob(jobId);
              }}
              onClose={() => setHistoryOpen(false)}
            />
          )}

          {!historyOpen && job && (
            <p className="font-stamp text-[10px] uppercase tracking-[0.2em] text-paper-muted">
              Dosya No. {job.id.slice(0, 8).toUpperCase()}
            </p>
          )}

          {!historyOpen && (state === "idle" || state === "error") && (
            <UploadDropzone disabled={isBusy} onStart={start} />
          )}

          {!historyOpen && state === "uploading" && (
            <p className="font-stamp text-xs uppercase tracking-[0.15em] text-stamp-pending animate-pulse-slow">
              Video yükleniyor…
            </p>
          )}

          {!historyOpen && error && (
            <p className="rounded-sm border border-stamp-failed/40 bg-stamp-failed/10 px-3 py-2 font-body text-sm text-stamp-failed">
              {error}
            </p>
          )}

          {!historyOpen && (state === "polling" || state === "cancelling" || state === "done" || state === "error") && job && (
            <section className="space-y-3 border-t border-ink-border pt-6">
              <h2 className="font-stamp text-xs uppercase tracking-[0.2em] text-paper-muted">Süreç</h2>
              <StageStamps status={job.status} errorMessage={job.errorMessage} />
              {(state === "polling" || state === "cancelling") && (
                <ProgressBar percent={job.progressPercent} message={job.progressMessage} />
              )}
            </section>
          )}

          {!historyOpen && (state === "polling" || state === "cancelling") && (
            <button
              type="button"
              onClick={cancel}
              disabled={state === "cancelling"}
              className="w-full rounded-sm border border-stamp-failed/50 py-2.5 font-stamp text-xs font-semibold uppercase tracking-[0.15em] text-stamp-failed transition-colors hover:bg-stamp-failed/10 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {state === "cancelling" ? "İptal ediliyor…" : "İşlemi İptal Et"}
            </button>
          )}

          {!historyOpen && state === "done" && job?.fullTextCensored && (
            <section className="space-y-3 border-t border-ink-border pt-6">
              <h2 className="font-stamp text-xs uppercase tracking-[0.2em] text-paper-muted">
                Sansürlü Metin
              </h2>
              <RedactedTranscript fullTextCensored={job.fullTextCensored} jobId={job.id} />
            </section>
          )}

          {!historyOpen && state === "done" && job && (
            <section className="space-y-4 border-t border-ink-border pt-6">
              <h2 className="font-stamp text-xs uppercase tracking-[0.2em] text-paper-muted">
                Dışa Aktar (MAXQDA / NVivo için)
              </h2>

              {job.fullTextCensored && (
                <div className="space-y-1.5">
                  <p className="font-stamp text-[10px] uppercase tracking-[0.15em] text-stamp-completed">
                    Sansürlü Metin
                  </p>
                  <div className="flex flex-wrap gap-2">
                    <button
                      type="button"
                      onClick={() =>
                        downloadTextAsDocx(
                          `sansurlu-metin-${job.id.slice(0, 8)}`,
                          "Sansürlü Görüşme Metni",
                          job.fullTextCensored!,
                        )
                      }
                      className="inline-flex items-center gap-1.5 rounded-sm border border-stamp-completed/60 px-3 py-1.5 font-stamp text-[10px] font-semibold uppercase tracking-[0.1em] text-stamp-completed transition-colors hover:bg-stamp-completed/10"
                    >
                      ⬇ Word
                    </button>
                    <button
                      type="button"
                      onClick={() =>
                        downloadTextAsTxt(`sansurlu-metin-${job.id.slice(0, 8)}`, job.fullTextCensored!)
                      }
                      className="inline-flex items-center gap-1.5 rounded-sm border border-stamp-completed/60 px-3 py-1.5 font-stamp text-[10px] font-semibold uppercase tracking-[0.1em] text-stamp-completed transition-colors hover:bg-stamp-completed/10"
                    >
                      ⬇ TXT
                    </button>
                    <button
                      type="button"
                      onClick={() =>
                        downloadTextAsXlsx(
                          `sansurlu-metin-${job.id.slice(0, 8)}`,
                          "Sansürlü Metin",
                          job.fullTextCensored!,
                        )
                      }
                      className="inline-flex items-center gap-1.5 rounded-sm border border-stamp-completed/60 px-3 py-1.5 font-stamp text-[10px] font-semibold uppercase tracking-[0.1em] text-stamp-completed transition-colors hover:bg-stamp-completed/10"
                    >
                      ⬇ Excel
                    </button>
                  </div>
                </div>
              )}

              {job.fullText && (
                <div className="space-y-1.5">
                  <p className="font-stamp text-[10px] uppercase tracking-[0.15em] text-stamp-failed">
                    Orijinal Metin
                  </p>
                  <div className="flex flex-wrap gap-2">
                    <button
                      type="button"
                      onClick={() =>
                        downloadTextAsDocx(
                          `orijinal-metin-${job.id.slice(0, 8)}`,
                          "Orijinal (Sansürsüz) Görüşme Metni",
                          job.fullText!,
                        )
                      }
                      className="inline-flex items-center gap-1.5 rounded-sm border border-stamp-failed/60 px-3 py-1.5 font-stamp text-[10px] font-semibold uppercase tracking-[0.1em] text-stamp-failed transition-colors hover:bg-stamp-failed/10"
                    >
                      ⬇ Word
                    </button>
                    <button
                      type="button"
                      onClick={() => downloadTextAsTxt(`orijinal-metin-${job.id.slice(0, 8)}`, job.fullText!)}
                      className="inline-flex items-center gap-1.5 rounded-sm border border-stamp-failed/60 px-3 py-1.5 font-stamp text-[10px] font-semibold uppercase tracking-[0.1em] text-stamp-failed transition-colors hover:bg-stamp-failed/10"
                    >
                      ⬇ TXT
                    </button>
                    <button
                      type="button"
                      onClick={() =>
                        downloadTextAsXlsx(
                          `orijinal-metin-${job.id.slice(0, 8)}`,
                          "Orijinal Metin",
                          job.fullText!,
                        )
                      }
                      className="inline-flex items-center gap-1.5 rounded-sm border border-stamp-failed/60 px-3 py-1.5 font-stamp text-[10px] font-semibold uppercase tracking-[0.1em] text-stamp-failed transition-colors hover:bg-stamp-failed/10"
                    >
                      ⬇ Excel
                    </button>
                  </div>
                  <p className="font-body text-xs text-stamp-failed/90">
                    ⚠ Orijinal metin kişisel veri (isim, yer vb.) içerir — bu dosyayı e-posta ile
                    paylaşmayın veya ortak bir klasöre koymayın, sadece kendi güvenli arşivinizde
                    saklayın.
                  </p>
                </div>
              )}
            </section>
          )}

          {!historyOpen && (state === "done" || state === "error") && (
            <button
              type="button"
              onClick={reset}
              className="font-stamp text-xs uppercase tracking-[0.15em] text-paper-muted underline decoration-dotted underline-offset-4 transition-colors hover:text-paper"
            >
              Yeni kayıt işle
            </button>
          )}
        </main>
      </div>
    </div>
  );
}
