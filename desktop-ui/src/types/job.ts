export type JobStatus = "Pending" | "Processing" | "Completed" | "Failed";

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
  createdAtUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
}
