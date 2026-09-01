import { useCallback, useEffect, useRef, useState } from "react";
import { getJobStatus, uploadInterviewVideo } from "../api/interviews";
import type { TranscriptionJobStatus } from "../types/job";

const POLL_INTERVAL_MS = 3000;

export type UploadState = "idle" | "uploading" | "polling" | "done" | "error";

export function useTranscriptionJob() {
  const [state, setState] = useState<UploadState>("idle");
  const [job, setJob] = useState<TranscriptionJobStatus | null>(null);
  const [error, setError] = useState<string | null>(null);

  // setInterval'in ID'sini render'lar arasında kaybetmemek için ref kullanıyoruz.
  const pollTimerRef = useRef<number | null>(null);

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

  const reset = useCallback(() => {
    stopPolling();
    setState("idle");
    setJob(null);
    setError(null);
  }, [stopPolling]);

  return { state, job, error, start, reset };
}
