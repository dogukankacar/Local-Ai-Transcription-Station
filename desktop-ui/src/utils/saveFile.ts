/**
 * Tauri penceresi içindeyken (WebView2), tarayıcının klasik blob+<a download>
 * numarası SESSİZCE ÇALIŞMIYOR -- Tauri bu navigasyonu güvenlik gereği
 * engelliyor. Bu yüzden Tauri içindeysek onun KENDİ dosya sistemi API'sini
 * (gerçek bir "Farklı Kaydet" penceresi açan) kullanıyoruz. Düz tarayıcıda
 * (ör. `npm run dev` ile test ederken) hâlâ eski yöntem çalışıyor, onu
 * bozmadan bırakıyoruz.
 */
export async function saveFile(filename: string, blob: Blob): Promise<void> {
  const isTauri = "__TAURI_INTERNALS__" in window || "__TAURI__" in window;

  if (isTauri) {
    const { save } = await import("@tauri-apps/plugin-dialog");
    const { writeFile } = await import("@tauri-apps/plugin-fs");

    const path = await save({ defaultPath: filename });
    if (!path) return; // kullanıcı "Farklı Kaydet" penceresini iptal etti

    const arrayBuffer = await blob.arrayBuffer();
    await writeFile(path, new Uint8Array(arrayBuffer));
    return;
  }

  // Düz tarayıcı (Tauri dışı) -- eski blob+anchor yöntemi.
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
