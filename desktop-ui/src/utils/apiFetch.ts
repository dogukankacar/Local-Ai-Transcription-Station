/**
 * NOT: Tauri 2.0'dan itibaren Windows'ta varsayılan köken (origin) artık
 * http://tauri.localhost -- https DEĞİL. Yani "karışık içerik" (mixed
 * content) engeli hiç söz konusu değildi; asıl sorun CORS listemizde bu
 * gerçek kökenin eksik olmasıydı (bkz. Program.cs). Bu yüzden burada
 * normal tarayıcı fetch()'ini kullanmak yeterli ve daha güvenilir --
 * Tauri'nin HTTP eklentisi (özellikle dosya/FormData yüklemede) ekstra
 * uyumluluk sorunlarına yol açabiliyor, gereksiz karmaşıklığı kaldırdık.
 */
export async function apiFetch(input: string, init?: RequestInit): Promise<Response> {
  return fetch(input, init);
}
