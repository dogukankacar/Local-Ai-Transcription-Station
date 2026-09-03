#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
  tauri::Builder::default()
    .plugin(tauri_plugin_fs::init())
    .plugin(tauri_plugin_dialog::init())
    .plugin(tauri_plugin_http::init())
    .on_window_event(|_window, event| {
      if let tauri::WindowEvent::CloseRequested { .. } = event {
        // Ana pencere kapatilirken, arka planda calisan Python AI motorunu
        // ve .NET API'yi de birlikte kapatiyoruz -- kullanicinin arkada
        // unutulmus process biriktirmesini onlemek icin.
        #[cfg(target_os = "windows")]
        {
          use std::os::windows::process::CommandExt;
          const CREATE_NO_WINDOW: u32 = 0x08000000;

          let _ = std::process::Command::new("taskkill")
            .args(["/F", "/IM", "WebAPI.exe"])
            .creation_flags(CREATE_NO_WINDOW)
            .output();

          let _ = std::process::Command::new("taskkill")
            .args(["/F", "/IM", "transcribe_censor_service.exe"])
            .creation_flags(CREATE_NO_WINDOW)
            .output();
        }
      }
    })
    .setup(|app| {
      if cfg!(debug_assertions) {
        app.handle().plugin(
          tauri_plugin_log::Builder::default()
            .level(log::LevelFilter::Info)
            .build(),
        )?;
      }
      Ok(())
    })
    .run(tauri::generate_context!())
    .expect("error while running tauri application");
}