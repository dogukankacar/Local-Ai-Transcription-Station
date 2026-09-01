import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Tauri, dev sırasında sabit bir porta bağlanmayı bekler (varsayılan 1420).
// Bu değeri tauri.conf.json'daki devUrl/devPath ile birebir eşleştir.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 1420,
    strictPort: true,
  },
});
