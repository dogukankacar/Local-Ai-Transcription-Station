import { Document, Packer, Paragraph, TextRun, HeadingLevel } from "docx";

/**
 * Düz metni bir Word (.docx) dosyasına çevirir ve tarayıcıda indirme
 * işlemini başlatır. Tamamen tarayıcı tarafında çalışır -- backend'e
 * ek bir istek gitmez, dosya hiç sunucudan geçmez.
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
  const url = URL.createObjectURL(blob);

  const link = document.createElement("a");
  link.href = url;
  link.download = filename.endsWith(".docx") ? filename : `${filename}.docx`;
  document.body.appendChild(link);
  link.click();
  link.remove();

  URL.revokeObjectURL(url);
}
