import { useRef, useState, type DragEvent } from "react";

interface Props {
  disabled: boolean;
  onStart: (file: File, language: string, diarization: boolean) => void;
}

function formatBytes(bytes: number): string {
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function UploadDropzone({ disabled, onStart }: Props) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [dragOver, setDragOver] = useState(false);
  // Varsayılan KAPALI -- konuşmacı ayrımı (pyannote) uzun kayıtlarda en
  // büyük süre kaynağı; sadece gerçekten gerekince açılmalı.
  const [diarization, setDiarization] = useState(false);

  const handleFiles = (files: FileList | null) => {
    const picked = files?.[0];
    if (picked) setFile(picked);
  };

  const handleDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setDragOver(false);
    if (!disabled) handleFiles(e.dataTransfer.files);
  };

  return (
    <div className="space-y-4">
      <div
        onDragOver={(e) => {
          e.preventDefault();
          if (!disabled) setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={handleDrop}
        onClick={() => !disabled && inputRef.current?.click()}
        className={`group flex flex-col items-center justify-center gap-2 rounded-sm border-2 border-dashed px-6 py-10 text-center transition-colors ${
          disabled ? "cursor-not-allowed border-ink-border/60 opacity-50" : "cursor-pointer"
        } ${dragOver ? "border-stamp-pending bg-stamp-pending/5" : "border-ink-border hover:border-paper-muted"}`}
      >
        <input
          ref={inputRef}
          type="file"
          accept="video/*,audio/*,.mp3,.wav"
          className="hidden"
          disabled={disabled}
          onChange={(e) => handleFiles(e.target.files)}
        />
        <span className="font-stamp text-xs uppercase tracking-[0.2em] text-paper-muted">
          {file ? "Dosya seçildi" : "Video veya ses dosyasını buraya sürükleyin"}
        </span>
        {file ? (
          <span className="max-w-full truncate font-body text-sm text-paper">
            {file.name} · {formatBytes(file.size)}
          </span>
        ) : (
          <span className="font-body text-sm text-paper-muted">
            veya tıklayıp seçin — MP4, MOV, MKV, WebM, MP3, WAV
          </span>
        )}
      </div>

      <label className="flex cursor-pointer items-start gap-2.5 font-body text-sm text-paper">
        <input
          type="checkbox"
          checked={diarization}
          onChange={(e) => setDiarization(e.target.checked)}
          disabled={disabled}
          className="mt-0.5 h-4 w-4 accent-stamp-pending"
        />
        <span>
          Konuşmacı Ayrımı Yap
          <span className="block font-body text-xs text-paper-muted">
            [Konuşmacı 1], [Konuşmacı 2] etiketleri ekler — uzun kayıtlarda işlem süresini
            belirgin şekilde uzatır.
          </span>
        </span>
      </label>

      <button
        type="button"
        disabled={disabled || !file}
        onClick={() => file && onStart(file, "tr", diarization)}
        className="w-full rounded-sm bg-stamp-pending/90 py-3 font-stamp text-sm font-semibold uppercase tracking-[0.2em] text-ink-bg transition-colors hover:bg-stamp-pending disabled:cursor-not-allowed disabled:bg-ink-border disabled:text-paper-muted"
      >
        İşlemi Başlat
      </button>
    </div>
  );
}
