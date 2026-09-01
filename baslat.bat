@echo off
echo Psikoloji Lokal Desifre Istasyonu Baslatiliyor...

:: 1. Veritabanini arka planda baslat (src/WebAPI icinde)
echo Postgres veritabani hazirlaniyor...
cd src\WebAPI
docker-compose up -d
cd ..\..

:: 2. Python Yapay Zeka Motorunu ayri pencerede baslat (Ana dizinde)
echo Python AI Motoru baslatiliyor...
start "Python AI Motoru" cmd /k "python -m uvicorn transcribe_censor_service:app --host 127.0.0.1 --port 8500"

:: 3. C# Backend API'yi ayri pencerede baslat
echo C# API baslatiliyor...
start "C# Backend" cmd /k "cd src\WebAPI && dotnet run"

:: 4. React Frontend'i ayri pencerede baslat (desktop-ui icinde)
echo React Arayuzu baslatiliyor...
start "React UI" cmd /k "cd desktop-ui && npm run dev"

echo Tum sistemler basariyla tetiklendi!
exit