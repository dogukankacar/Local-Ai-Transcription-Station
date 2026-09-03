import { saveFile } from "./saveFile";

export async function downloadTextAsTxt(filename: string, text: string): Promise<void> {
  const blob = new Blob([text], { type: "text/plain;charset=utf-8" });
  await saveFile(filename.endsWith(".txt") ? filename : `${filename}.txt`, blob);
}
