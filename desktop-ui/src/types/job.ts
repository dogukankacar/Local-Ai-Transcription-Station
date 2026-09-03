export type JobStatus = "Pending" | "Processing" | "Completed" | "Failed" | "Cancelled";

// C# TranscriptionJobStatusDto ile birebir eşleşir (ASP.NET Core varsayılan
// olarak camelCase JSON serileştirir).
export interface TranscriptionJobStatus {
  id: string;
  status: JobStatus;
  srtFilePath: string | null;
  /** DİKKAT: sansürsüz orijinal metin -- kişisel veri içerir. */
  fullText: string | null;
  fullTextCensored: string | null;
  errorMessage: string | null;
  audioDurationSeconds: number | null;
  progressPercent: number | null;
  progressMessage: string | null;
  createdAtUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
}

// C# TranscriptionJobSummaryDto ile birebir eşleşir -- geçmiş listesi için
// hafif özet (tam metinleri içermez).
export interface TranscriptionJobSummary {
  id: string;
  status: JobStatus;
  originalFileName: string | null;
  diarization: boolean;
  audioDurationSeconds: number | null;
  createdAtUtc: string;
  completedAtUtc: string | null;
}

// C# PagedJobsResultDto ile birebir eşleşir.
export interface PagedJobsResult {
  items: TranscriptionJobSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
}
