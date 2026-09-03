import { useCallback, useEffect, useRef, useState } from "react";
import { cancelJob, getJobStatus, uploadInterviewVideo } from "../api/interviews";
import type { TranscriptionJobStatus } from "../types/job";

const POLL_INTERVAL_MS = 3000;

export type UploadState = "idle" | "uploading" | "polling" | "cancelling" | "done" | "error";

export function useTranscriptionJob() {
  const [state, setState] = useState<UploadState>("idle");
  const [job, setJob] = useState<TranscriptionJobStatus | null>(null);
  const [error, setError] = useState<string | null>(null);

  // setInterval'in ID'sini ve aktif jobId'yi render'lar arasında
  // kaybetmemek için ref kullanıyoruz.
  const pollTimerRef = useRef<number | null>(null);
  const activeJobIdRef = useRef<string | null>(null);

  const stopPolling = useCallback(() => {
    if (pollTimerRef.current !== null) {
      window.clearInterval(pollTimerRef.current);
      pollTimerRef.current = null;
    }
  }, []);

  // Bileşen unmount olursa (kullanıcı ekranı kapatırsa) interval sızıntısını önle.
  useEffect(() => stopPolling, [stopPolling]);

  const start = useCallback(
    async (file: File, language: string, diarization: boolean) => {
      setError(null);
      setJob(null);
      setState("uploading");

      try {
        const { jobId } = await uploadInterviewVideo(file, language, diarization);
        activeJobIdRef.current = jobId;
        setState("polling");

        pollTimerRef.current = window.setInterval(async () => {
          try {
            const status = await getJobStatus(jobId);
            setJob(status);

            if (status.status === "Completed") {
              stopPolling();
              setState("done");
            } else if (status.status === "Failed") {
              stopPolling();
              setState("error");
              setError(status.errorMessage ?? "İşlem başarısız oldu.");
            } else if (status.status === "Cancelled") {
              stopPolling();
              setState("idle"); // doğrudan boş ekrana dön, yeni dosya seçmeye hazır
            }
            // Pending / Processing ise sessizce bir sonraki tick'i bekle.
          } catch (pollErr) {
            stopPolling();
            setState("error");
            setError(pollErr instanceof Error ? pollErr.message : "Durum sorgulanırken hata oluştu.");
          }
        }, POLL_INTERVAL_MS);
      } catch (uploadErr) {
        setState("error");
        setError(uploadErr instanceof Error ? uploadErr.message : "Video yüklenirken hata oluştu.");
      }
    },
    [stopPolling],
  );

  const cancel = useCallback(async () => {
    const jobId = activeJobIdRef.current;
    if (!jobId) return;

    setState("cancelling");
    try {
      await cancelJob(jobId);
      // Polling'i durdurmuyoruz -- birkaç saniye içinde durum "Cancelled"
      // olarak görünecek ve yukarıdaki interval bunu doğal olarak yakalayıp
      // ekranı sıfırlayacak. Kullanıcı beklemeden hemen yeni bir dosya
      // seçebilsin diye burada da state'i idle'a çekiyoruz.
      stopPolling();
      setState("idle");
    } catch (cancelErr) {
      setState("polling"); // iptal isteği başarısız oldu, polling'e geri dön
      setError(cancelErr instanceof Error ? cancelErr.message : "İptal edilirken hata oluştu.");
    }
  }, [stopPolling]);

  const viewHistoricalJob = useCallback(async (jobId: string) => {
    stopPolling();
    activeJobIdRef.current = jobId;
    setError(null);

    try {
      const status = await getJobStatus(jobId);
      setJob(status);

      if (status.status === "Completed") {
        setState("done");
      } else if (status.status === "Failed") {
        setState("error");
        setError(status.errorMessage ?? "İşlem başarısız oldu.");
      } else {
        // Cancelled / hâlâ Pending-Processing (nadir bir edge case) --
        // geçmişten açılan bir kayıt için tekrar polling başlatmıyoruz,
        // sadece o anki durumu gösteriyoruz.
        setState("error");
        setError(`Bu iş "${status.status}" durumunda, görüntülenecek bir sonuç yok.`);
      }
    } catch (err) {
      setState("error");
      setError(err instanceof Error ? err.message : "Job yüklenirken hata oluştu.");
    }
  }, [stopPolling]);

  const reset = useCallback(() => {
    stopPolling();
    activeJobIdRef.current = null;
    setState("idle");
    setJob(null);
    setError(null);
  }, [stopPolling]);

  return { state, job, error, start, cancel, reset, viewHistoricalJob };
}
