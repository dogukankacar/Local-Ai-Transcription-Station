import type { PagedJobsResult, TranscriptionJobStatus } from "../types/job";
import { apiFetch } from "../utils/apiFetch";

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5169";

export async function uploadInterviewVideo(
  file: File,
  language: string,
  diarization: boolean,
): Promise<{ jobId: string }> {
  const formData = new FormData();
  // Alan adları, C# controller'daki parametre adlarıyla (videoFile,
  // language, diarization) birebir eşleşmeli.
  formData.append("videoFile", file);
  formData.append("language", language);
  formData.append("diarization", String(diarization));

  const response = await apiFetch(`${API_BASE_URL}/api/Interviews/process`, {
    method: "POST",
    body: formData,
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Video yüklenemedi (${response.status}): ${text || response.statusText}`);
  }

  return response.json();
}

export async function getJobStatus(jobId: string): Promise<TranscriptionJobStatus> {
  const response = await apiFetch(`${API_BASE_URL}/api/Interviews/jobs/${jobId}`);

  if (response.status === 404) {
    throw new Error("Job bulunamadı.");
  }
  if (!response.ok) {
    throw new Error(`Durum sorgulanamadı (${response.status})`);
  }

  return response.json();
}

export function getJobSrtDownloadUrl(jobId: string): string {
  return `${API_BASE_URL}/api/Interviews/jobs/${jobId}/srt`;
}

export async function cancelJob(jobId: string): Promise<void> {
  const response = await apiFetch(`${API_BASE_URL}/api/Interviews/jobs/${jobId}/cancel`, {
    method: "POST",
  });

  // 409: job zaten bitmiş bir durumdaydı (Completed/Failed/Cancelled) --
  // bu bir hata değil, polling zaten bir sonraki tick'te doğru durumu
  // gösterecek, o yüzden burada fırlatmıyoruz.
  if (!response.ok && response.status !== 409) {
    const text = await response.text();
    throw new Error(`İptal edilemedi (${response.status}): ${text || response.statusText}`);
  }
}

export async function listJobs(page = 1, pageSize = 20): Promise<PagedJobsResult> {
  const response = await apiFetch(
    `${API_BASE_URL}/api/Interviews/jobs?page=${page}&pageSize=${pageSize}`,
  );

  if (!response.ok) {
    throw new Error(`Geçmiş listesi alınamadı (${response.status})`);
  }

  return response.json();
}

export async function deleteJob(jobId: string): Promise<void> {
  const response = await apiFetch(`${API_BASE_URL}/api/Interviews/jobs/${jobId}`, {
    method: "DELETE",
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Silinemedi (${response.status}): ${text || response.statusText}`);
  }
}
