# Lokal Deşifre İstasyonu — Masaüstü UI

React + Vite + Tailwind CSS. Backend'den bağımsız çalışır, sadece
`VITE_API_BASE_URL` üzerinden C# API'sine konuşur (varsayılan: `http://localhost:5000`).

## 1) Web olarak test (Tauri kurmadan önce hızlı doğrulama)

```
npm install
npm run dev
```

Tarayıcıda `http://localhost:1420` açılır. C# API'sinin (`dotnet run`) ve
Python servisinin (`uvicorn ...`) da ayrı terminallerde çalışıyor olması gerekir.

## 2) Tauri'ye sarmalama (önerilen — hafif, native)

Bu klasörün İÇİNDE (package.json'ın yanında) çalıştır:

```
npm install -D @tauri-apps/cli
npx tauri init
```

`tauri init` birkaç soru soracak:
- App name: Lokal Deşifre İstasyonu
- Window title: aynısı
- Web assets location (dist klasörü): `../dist` değil, mevcut `dist` (varsayılanı kabul et)
- Dev server URL: `http://localhost:1420` (vite.config.ts'teki port ile eşleşmeli)
- Dev command: `npm run dev`
- Build command: `npm run build`

Sonra:

```
npm run tauri dev
```

native pencere açılmalı. Production build için: `npm run tauri build`
(çıktı `.msi`/`.exe` olarak `src-tauri/target/release/bundle/` altında olur).

## 3) Electron alternatifi

Tauri yerine Electron istersen, bu React kodunun hiçbiri değişmez — sadece
`electron` + `electron-builder` paketlerini ekleyip bir `electron/main.js`
dosyasıyla `dist/index.html`'i bir `BrowserWindow` içinde yüklemen yeterli.
Tauri'yi önermemin sebebi: çok daha küçük paket boyutu (Electron ~120MB+,
Tauri ~10MB) ve native webview kullanması — bu masaüstü aracın "hafif,
yerel, izole" felsefesiyle daha uyumlu.

## Notlar

- Backend'de CORS politikası (`DesktopApp`) sadece `http://localhost:1420`,
  `tauri://localhost` ve `https://tauri.localhost` origin'lerine izin
  veriyor. Farklı bir port/origin kullanırsan Program.cs'deki listeyi
  güncellemen gerekir.
- Polling aralığı `useTranscriptionJob.ts` içinde `POLL_INTERVAL_MS = 3000`
  (3 saniye) — çok sık gereksiz yük bindirir, çok seyrek UI'ı yavaş
  hissettirir, 3sn dengeli bir başlangıç noktası.
