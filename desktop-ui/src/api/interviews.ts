import type { TranscriptionJobStatus } from "../types/job";

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000";

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

  const response = await fetch(`${API_BASE_URL}/api/Interviews/process`, {
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
  const response = await fetch(`${API_BASE_URL}/api/Interviews/jobs/${jobId}`);

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
