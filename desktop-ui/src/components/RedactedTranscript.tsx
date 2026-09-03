import { getJobSrtDownloadUrl } from "../api/interviews";
import { saveFile } from "../utils/saveFile";
import { apiFetch } from "../utils/apiFetch";

interface Props {
  fullTextCensored: string;
  jobId: string;
}

export function RedactedTranscript({ fullTextCensored, jobId }: Props) {
  const parts = fullTextCensored.split(/(\[GİZLENDİ\])/g);

  const handleDownloadSrt = async () => {
    // Tauri içinde <a href download> sessizce çalışmıyor -- SRT içeriğini
    // kendimiz çekip saveFile() ile (Tauri'nin gerçek "Farklı Kaydet"
    // penceresini açan yöntemiyle) kaydediyoruz.
    const response = await apiFetch(getJobSrtDownloadUrl(jobId));
    const text = await response.text();
    const blob = new Blob([text], { type: "text/plain;charset=utf-8" });
    await saveFile(`${jobId.slice(0, 8)}.srt`, blob);
  };

  return (
    <div className="space-y-4">
      <div className="max-h-64 overflow-y-auto rounded-sm border border-ink-border bg-ink-panelAlt p-4 font-body text-sm leading-relaxed text-paper">
        {parts.map((part, i) =>
          part === "[GİZLENDİ]" ? (
            <span
              key={i}
              title="Kişisel veri gizlendi"
              className="mx-0.5 inline-block h-[0.9em] w-14 translate-y-[2px] rounded-[2px] bg-stamp-redaction align-middle"
            />
          ) : (
            <span key={i}>{part}</span>
          ),
        )}
      </div>

      <button
        type="button"
        onClick={handleDownloadSrt}
        className="inline-flex items-center gap-2 rounded-sm border border-stamp-completed/60 px-4 py-2 font-stamp text-xs font-semibold uppercase tracking-[0.15em] text-stamp-completed transition-colors hover:bg-stamp-completed/10"
      >
        ⬇ SRT Dosyasını İndir
      </button>
    </div>
  );
}
