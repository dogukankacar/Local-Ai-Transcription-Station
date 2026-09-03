import * as XLSX from "xlsx";
import { saveFile } from "./saveFile";

// "[Konuşmacı 1]: metin" satırlarını ayrıştırıp Konuşmacı/Metin sütunlarına
// bölüyor. Diarization kapalıysa (etiket yoksa) Konuşmacı sütunu boş kalır.
const SPEAKER_LINE_PATTERN = /^\[(.+?)\]:\s*(.*)$/;

export async function downloadTextAsXlsx(filename: string, sheetTitle: string, text: string): Promise<void> {
  const rows = text.split("\n").map((line) => {
    const match = line.match(SPEAKER_LINE_PATTERN);
    if (match) {
      return { Konuşmacı: match[1], Metin: match[2] };
    }
    return { Konuşmacı: "", Metin: line };
  });

  const worksheet = XLSX.utils.json_to_sheet(rows);
  worksheet["!cols"] = [{ wch: 16 }, { wch: 100 }];

  const workbook = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(workbook, worksheet, sheetTitle.slice(0, 31)); // Excel sekme adı sınırı: 31 karakter

  const arrayBuffer = XLSX.write(workbook, { bookType: "xlsx", type: "array" });
  const blob = new Blob([arrayBuffer], {
    type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
  });

  await saveFile(filename.endsWith(".xlsx") ? filename : `${filename}.xlsx`, blob);
}
