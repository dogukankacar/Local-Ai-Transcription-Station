import { Document, Packer, Paragraph, TextRun, HeadingLevel } from "docx";
import { saveFile } from "./saveFile";

/**
 * Düz metni bir Word (.docx) dosyasına çevirir ve kaydeder. Tamamen
 * tarayıcı/Tauri tarafında çalışır -- backend'e ek bir istek gitmez.
 */
export async function downloadTextAsDocx(
  filename: string,
  title: string,
  text: string,
): Promise<void> {
  const bodyParagraphs = text.split("\n").map(
    (line) =>
      new Paragraph({
        children: [new TextRun(line)],
        spacing: { after: 160 },
      }),
  );

  const doc = new Document({
    sections: [
      {
        children: [
          new Paragraph({
            text: title,
            heading: HeadingLevel.HEADING_1,
            spacing: { after: 300 },
          }),
          ...bodyParagraphs,
        ],
      },
    ],
  });

  const blob = await Packer.toBlob(doc);
  await saveFile(filename.endsWith(".docx") ? filename : `${filename}.docx`, blob);
}
