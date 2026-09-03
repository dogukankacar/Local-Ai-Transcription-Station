"""
Lokal ve İzole Nitel Araştırma İstasyonu - AI Motoru
------------------------------------------------------
Bu servis, C# .NET Backend'i tarafından localhost üzerinden çağrılan
bağımsız bir FastAPI mikroservisidir. Sadece 127.0.0.1'e bind olur,
dışarıya hiçbir istek atmaz (KVKK / Etik Kurul gereksinimi) -- tek istisna,
modellerin İLK çalıştırmada HuggingFace'ten bir kerelik indirilmesidir;
indirildikten sonra tamamen çevrimdışı çalışır.

Donanım hedefi: RTX 3060 6GB VRAM
Strateji:
  - faster-whisper -> GPU'da (VRAM'in büyük kısmı burada kullanılır)
  - Türkçe NER modeli -> CPU'da (VRAM'i whisper için serbest bırakmak amacıyla)
  - Speaker diarization (pyannote) -> CPU'da (aynı sebep: VRAM bütçesini
    whisper'a ayırmak, ayrıca CUDA'lı torch kurulumunun whisper'ın kendi
    CUDA DLL kurulumuyla çakışmasını tamamen ortadan kaldırmak için)

Çalıştırma:
    uvicorn transcribe_censor_service:app --host 127.0.0.1 --port 8500 --workers 1
    (workers=1 zorunlu: modeller tek instance olarak belleğe/VRAM'e
     yüklenir, birden fazla worker VRAM'i taşırır)
"""

from __future__ import annotations

import gc
import logging
import os
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from contextlib import asynccontextmanager
from dataclasses import dataclass
from pathlib import Path
from typing import Literal, Optional

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field


def _resolve_log_file_path() -> Path:
    """
    .exe'nin (paketlenmiş haldeyken) ya da bu .py dosyasının (geliştirme
    modundayken) yanına bir 'logs' klasörü açıp oraya yazar. Konsol
    penceresi KAPALI (console=False) paketlendiğinde stderr/stdout hiç
    yok -- eski haliyle (StreamHandler) log yazmaya çalışmak burada
    ÇÖKMEYE yol açardı. Dosyaya yazmak hem bu çökmeyi önlüyor hem de
    konsol penceresi olmadan da sorun teşhis edebilmeni sağlıyor.
    """
    if getattr(sys, "frozen", False):
        base_dir = Path(sys.executable).parent
    else:
        base_dir = Path(__file__).resolve().parent
    log_dir = base_dir / "logs"
    log_dir.mkdir(exist_ok=True)
    return log_dir / "transcribe_censor_service.log"


logging.basicConfig(
    level=logging.INFO,
    filename=str(_resolve_log_file_path()),
    filemode="a",
    format="%(asctime)s %(levelname)s:%(name)s:%(message)s",
    encoding="utf-8",
)
logger = logging.getLogger("ner-transcribe")

# FastAPI, senkron ("def", "async def" değil) endpoint'leri otomatik olarak
# AYRI THREAD'LERDE çalıştırır -- yani birden fazla istek aynı anda gelirse,
# hepsi paralel olarak whisper/NER/diarization modellerine erişmeye
# çalışır. Bu modeller (özellikle tek bir GPU'daki whisper) eşzamanlı
# kullanım için tasarlanmadı -- iki iş aynı anda GPU'ya girerse hem
# birbirinin verisini bozabilir hem de sürücü seviyesinde kararsızlığa
# katkıda bulunabilir. Bu kilit, kaç istek aynı anda gelirse gelsin GERÇEK
# işlemenin HER ZAMAN tek seferde, sırayla yapılmasını garanti eder.
_PROCESSING_LOCK = threading.Lock()


def _log_system_memory(context: str) -> None:
    """
    PowerShell/WMI üzerinden GERÇEK sistem RAM kullanımını loglar (VRAM
    logundaki nvidia-smi ile aynı mantık: harici, güvenilir bir kaynaktan
    doğrudan ölçüm -- Python'un kendi bellek görünümüne değil).
    """
    try:
        cmd = (
            '$os = Get-CimInstance Win32_OperatingSystem; '
            '$usedGB = [math]::Round(($os.TotalVisibleMemorySize - $os.FreePhysicalMemory)/1MB,2); '
            '$totalGB = [math]::Round($os.TotalVisibleMemorySize/1MB,2); '
            'Write-Output "$usedGB,$totalGB"'
        )
        result = subprocess.run(
            ["powershell", "-NoProfile", "-Command", cmd],
            capture_output=True, text=True, timeout=10, check=True,
        )
        used_gb, total_gb = (x.strip() for x in result.stdout.strip().split(","))
        pct = float(used_gb) / float(total_gb) * 100
        logger.info("[RAM %s] %s GB / %s GB kullanımda (%%%.0f)", context, used_gb, total_gb, pct)
    except Exception as exc:
        logger.debug("Sistem RAM'i okunamadı (kritik değil): %s", exc)


def _log_gpu_memory(context: str) -> None:
    """
    nvidia-smi'yi çağırarak GERÇEK VRAM kullanımını loglar.

    torch.cuda.empty_cache()/memory_allocated() KULLANMIYORUZ, çünkü bu
    serviste torch BİLEREK CPU-only kurulu (pyannote için) -- CUDA'lı torch
    kurulumu whisper'ın kendi ctranslate2/CUDA DLL kurulumuyla çakışıyordu.
    Ayrıca whisper zaten torch değil ctranslate2 kullanıyor; torch.cuda.*
    çağrıları whisper'ın belleği üzerinde zaten hiçbir etkiye sahip olmazdı.

    nvidia-smi ise hangi kütüphanenin ayırdığından tamamen bağımsız,
    doğrudan NVIDIA sürücüsünden GERÇEK cihaz belleğini okur -- bu yüzden
    ctranslate2 (whisper) tarafından kullanılan VRAM'i de doğru gösterir.
    """
    try:
        result = subprocess.run(
            ["nvidia-smi", "--query-gpu=memory.used,memory.total", "--format=csv,noheader,nounits"],
            capture_output=True, text=True, timeout=5, check=True,
        )
        used_mb, total_mb = (x.strip() for x in result.stdout.strip().split(","))
        logger.info("[VRAM %s] %s MB / %s MB kullanımda", context, used_mb, total_mb)
    except Exception as exc:
        logger.debug("nvidia-smi ile VRAM okunamadı (kritik değil): %s", exc)


# C# API'sinin adresi -- Python buraya ilerleme bildirimleri POST'lar.
# Aynı localhost üzerinde çalıştıkları için sabit bir varsayılan yeterli,
# ama farklı bir port kullanıyorsan ortam değişkeniyle geçersiz kılabilirsin.
_CSHARP_API_BASE_URL = os.environ.get("CSHARP_API_BASE_URL", "http://127.0.0.1:5169")


def _report_progress(job_id: str, percent: int, message: str) -> None:
    """
    C#'a "şu an %X'teyim" diye haber verir. TAMAMEN best-effort: bu çağrı
    başarısız olursa (C# o an ayakta değilse, ağ sorunu vb.) transkripsiyonun
    kendisini ASLA etkilememeli -- sadece sessizce loglayıp devam ediyoruz.
    job_id boşsa (ör. Swagger'dan job_id vermeden elle test ediliyorsa) hiç
    denemiyoruz.
    """
    if not job_id:
        return
    try:
        import httpx
        httpx.post(
            f"{_CSHARP_API_BASE_URL}/api/Interviews/jobs/{job_id}/progress",
            json={"percent": percent, "message": message},
            timeout=2.0,
        )
    except Exception as exc:
        logger.debug("İlerleme bildirimi gönderilemedi (kritik değil): %s", exc)

# --------------------------------------------------------------------------
# 0) CUDA 12 DLL YOLLARI (Windows + CUDA 13 sistem kurulumu ile uyumluluk için)
# --------------------------------------------------------------------------
# ctranslate2 (faster-whisper'ın motoru) CUDA 12.x cuBLAS/cuDNN DLL'lerini
# arar. Sistemde CUDA 13 kuruluysa cublas64_13.dll üretir ve WhisperModel()
# çağrısı "cublas64_12.dll not found" hatasıyla patlar.
#
# Çözüm: sistem CUDA kurulumuna dokunmadan, venv/kullanıcı ortamına pip ile
# kurulan CUDA 12 DLL'lerini bu process'e özel olarak PATH'e ekliyoruz:
#
#   pip install nvidia-cublas-cu12 nvidia-cudnn-cu12
#
# NOT: pyannote/torch İÇİN AYRI bir CUDA kurulumuna gerek YOK -- diarization
# bilerek CPU'da çalıştığından, torch'u CPU-only kurman yeterli (bkz. kurulum
# talimatları). Bu blok sadece whisper'ın CUDA ihtiyacı için var.
if sys.platform == "win32" and os.environ.get("SKIP_CUDA_DLL_PRELOAD") != "1":
    import ctypes
    import site

    _candidate_roots: list[Path] = [Path(sys.prefix) / "Lib" / "site-packages"]
    try:
        _candidate_roots.append(Path(site.getusersitepackages()))
    except Exception:
        pass
    try:
        _candidate_roots.extend(Path(p) for p in site.getsitepackages())
    except Exception:
        pass

    _dll_dirs: list[Path] = []
    for _root in _candidate_roots:
        _nvidia_root = _root / "nvidia"
        if not _nvidia_root.is_dir():
            continue
        for _pkg_dir in _nvidia_root.iterdir():
            _dll_dir = _pkg_dir / "bin"
            if _dll_dir.is_dir():
                os.add_dll_directory(str(_dll_dir))
                logger.info("CUDA DLL dizini eklendi: %s", _dll_dir)
                _dll_dirs.append(_dll_dir)

    if not _dll_dirs:
        logger.warning(
            "Hiçbir CUDA DLL dizini bulunamadı (taranan kökler: %s). "
            "'pip install nvidia-cublas-cu12 nvidia-cudnn-cu12' kurulumu "
            "tamamlandı mı ve doğru Python/pip ile mi kuruldu?",
            [str(r) for r in _candidate_roots],
        )
    else:
        # SADECE PATH'e eklemek yeterli değil: torch (pyannote üzerinden)
        # import edildiğinde kendi bundled DLL'leriyle aynı isimli bir DLL'i
        # ÖNCE kendisi belleğe yükleyebiliyor, bu da whisper'ın (ctranslate2)
        # o DLL'i ilk kullandığı an bizim doğru/uyumlu sürümümüz yerine
        # torch'un kopyasını (ya da bozuk bir kombinasyonu) bulmasına yol
        # açabiliyor. Bunun önüne geçmek için doğru DLL'leri BURADA, henüz
        # hiçbir şey (özellikle torch) import edilmeden ÖNCE, tam yol
        # vererek elle yüklüyoruz -- Windows aynı isimli bir DLL zaten
        # belleğe yüklendiğinde sonraki LoadLibrary çağrılarında o kopyayı
        # kullanmaya devam eder.
        #
        # NOT: Yeni (torch>=2.8) kurulumlarda bu preload mekanizması torch'un
        # KENDİ bundled DLL'leriyle çakışıp "WinError 127: specified
        # procedure could not be found" hatasına yol açabiliyor -- bu
        # durumda SKIP_CUDA_DLL_PRELOAD=1 ile tamamen atlanabilir (whisper
        # hâlâ CUDA'yı kullanabiliyor çünkü torch zaten kendi DLL'lerini
        # doğru yüklüyor).
        _priority_order = ["nvjitlink", "cublaslt", "cublas", "cudnn"]

        def _priority(p: Path) -> int:
            name = p.name.lower()
            for i, prefix in enumerate(_priority_order):
                if prefix in name:
                    return i
            return len(_priority_order)

        _all_dlls = sorted(
            (dll for d in _dll_dirs for dll in d.glob("*.dll")),
            key=_priority,
        )

        _preloaded = 0
        for _dll_path in _all_dlls:
            try:
                ctypes.WinDLL(str(_dll_path))
                _preloaded += 1
            except OSError as _exc:
                # Bazı DLL'ler başka birine bağımlı olduğu için ilk denemede
                # başarısız olabilir, bu normaldir -- kritik olanı en azından
                # cublas/cudnn/nvjitlink'in başarıyla yüklenmiş olması.
                logger.debug("DLL önceden yüklenemedi (göz ardı edilebilir): %s (%s)", _dll_path.name, _exc)

        logger.info("%d/%d CUDA DLL'i önceden belleğe yüklendi.", _preloaded, len(_all_dlls))

# --- FFmpeg SHARED DLL'leri (torchcodec/pyannote için) ---
# BİLEREK yukarıdaki CUDA preload bloğunun DIŞINDA -- SKIP_CUDA_DLL_PRELOAD=1
# ayarlansa bile bu blok HER ZAMAN çalışmalı, çünkü torchcodec'in ses
# decode edebilmesi (dolayısıyla diarization'ın hiç çalışması) buna bağlı,
# CUDA preload'la hiçbir ilgisi yok.
# torchcodec, ffmpeg.exe'yi DEĞİL, FFmpeg'in paylaşımlı kütüphane (DLL)
# sürümünü arar (avcodec-*.dll, avformat-*.dll vb.).


def _default_ffmpeg_shared_dir() -> Path:
    """
    .exe'nin (paketlenmiş haldeyken) ya da bu .py dosyasının (geliştirme
    modundayken) bulunduğu klasöre göre, yanına konacak bir 'ffmpeg\\bin'
    klasörünü otomatik bulur -- taşınabilir dağıtımda kimsenin elle
    FFMPEG_SHARED_DIR ortam değişkeni ayarlamasına gerek kalmasın diye.
    Dağıtım klasörünü hazırlarken FFmpeg'in "shared" build'ini bu .exe'nin
    yanına 'ffmpeg' adında bir klasöre koyman yeterli olacak.
    """
    if getattr(sys, "frozen", False):
        base_dir = Path(sys.executable).parent
    else:
        base_dir = Path(__file__).resolve().parent
    return base_dir / "ffmpeg" / "bin"


if sys.platform == "win32":
    _ffmpeg_shared_dir = os.environ.get("FFMPEG_SHARED_DIR")
    if not _ffmpeg_shared_dir:
        _auto_dir = _default_ffmpeg_shared_dir()
        if _auto_dir.is_dir():
            _ffmpeg_shared_dir = str(_auto_dir)
            logger.info("FFMPEG_SHARED_DIR ayarlı değil, otomatik bulundu: %s", _ffmpeg_shared_dir)

    if _ffmpeg_shared_dir:
        if Path(_ffmpeg_shared_dir).is_dir():
            os.add_dll_directory(_ffmpeg_shared_dir)
            logger.info("FFmpeg shared DLL dizini eklendi: %s", _ffmpeg_shared_dir)
        else:
            logger.warning(
                "FFMPEG_SHARED_DIR ayarlı ama klasör bulunamadı: %s", _ffmpeg_shared_dir
            )
    else:
        logger.warning(
            "FFmpeg shared DLL dizini bulunamadı (ne FFMPEG_SHARED_DIR ayarlı, "
            "ne de .exe'nin yanında bir 'ffmpeg\\bin' klasörü var) -- "
            "konuşmacı ayrımı (diarization) ses okurken hata verebilir."
        )

# --------------------------------------------------------------------------
# 1) MODEL YÖNETİMİ (uygulama başlarken bir kere yüklenir, her istekte değil)
# --------------------------------------------------------------------------

_MODELS: dict = {}

# DIARIZATION_DEVICE:
#   "cpu" (VARSAYILAN, güvenli) -- whisper GPU'da kalıcı yüklü kalır,
#     diarization CPU'da whisper ile PARALEL çalışır. CUDA'lı torch
#     GEREKMEZ, mevcut kararlı kurulumla hiçbir şey değişmez.
#   "gpu" (opsiyonel, daha hızlı ama daha riskli) -- diarization'ı GPU'da
#     çalıştırır ama whisper ile ASLA aynı anda değil: her istekte önce
#     whisper GPU'da çalışır ve biter, whisper GERÇEKTEN bellekten silinir
#     (del + gc.collect() -- ctranslate2'nin kendi CUDA belleği torch
#     komutlarıyla temizlenemez, bu yüzden nesnenin kendisini yok ediyoruz),
#     SONRA diarization GPU'ya taşınıp çalışır, SONRA o da CPU'ya geri
#     taşınıp torch.cuda.empty_cache() ile VRAM'i serbest bırakır, SONRA
#     whisper bir sonraki istek için yeniden yüklenir. İki model ASLA aynı
#     anda VRAM'de resident+aktif olmuyor. Bunun için CUDA'lı bir torch
#     kurulumu GEREKİR (bkz. kurulum notları) -- bu, whisper'ın CUDA DLL
#     kurulumuyla teorik olarak çakışabilir, dikkatli test edilmeli.
_DIARIZATION_DEVICE = os.environ.get("DIARIZATION_DEVICE", "cpu").strip().lower()
if _DIARIZATION_DEVICE not in ("cpu", "gpu"):
    logger.warning("Geçersiz DIARIZATION_DEVICE değeri: %s -- 'cpu' kullanılacak.", _DIARIZATION_DEVICE)
    _DIARIZATION_DEVICE = "cpu"


def _load_whisper():
    from faster_whisper import WhisperModel

    # Ortam değişkeniyle değiştirilebilir -- A/B test için kod değiştirmeden
    # "large-v3-turbo" deneyebilirsin (bkz. WHISPER_MODEL ortam değişkeni).
    model_size = os.environ.get("WHISPER_MODEL", "large-v3")
    download_root = str(Path.home() / ".cache" / "whisper-models")

    # WHISPER_DEVICE ile elle zorlamak istersen ("cuda" ya da "cpu") --
    # aksi halde önce GPU'yu dener, bulunamazsa/uyumsuzsa OTOMATİK olarak
    # CPU'ya düşer. Bu, akademisyenin ekran kartsız (ya da uyumsuz sürücülü)
    # bir bilgisayarında uygulamanın ÇÖKMEK yerine yavaş da olsa çalışmasını
    # sağlıyor -- taşınabilir dağıtımda kimin bilgisayarında ne olduğunu
    # bilemeyiz, bu yüzden koda bırakıyoruz.
    forced_device = os.environ.get("WHISPER_DEVICE")
    if forced_device in ("cuda", "cpu"):
        candidates = [(forced_device, "int8_float16" if forced_device == "cuda" else "int8")]
    else:
        candidates = [("cuda", "int8_float16"), ("cpu", "int8")]

    last_error: Exception | None = None
    for device, compute_type in candidates:
        try:
            model = WhisperModel(
                model_size_or_path=model_size,
                device=device,
                compute_type=compute_type,
                download_root=download_root,
            )
            logger.info("faster-whisper yüklendi (%s, %s, %s)", model_size, compute_type, device)
            if device == "cpu" and not forced_device:
                logger.warning(
                    "GPU bulunamadı ya da kullanılamadı -- whisper CPU modunda "
                    "çalışacak. Bu, işlem süresini önemli ölçüde (10-20 kat) "
                    "uzatabilir ama uygulamanın ekran kartsız bilgisayarlarda da "
                    "çökmeden çalışmasını sağlıyor."
                )
            return model
        except Exception as exc:
            last_error = exc
            logger.warning(
                "Whisper '%s' cihazında yüklenemedi (%s) -- alternatif deneniyor.",
                device, exc,
            )

    raise RuntimeError(f"Whisper hiçbir cihazda yüklenemedi (son hata: {last_error})") from last_error


def _unload_whisper() -> None:
    """
    Whisper model nesnesini GERÇEKTEN bellekten siler. torch.cuda.empty_cache()
    burada İŞE YARAMAZ (whisper ctranslate2 kullanıyor, torch değil) --
    ctranslate2'nin CUDA belleğini serbest bırakmanın tek yolu nesnenin
    kendisini yok etmek (del + gc.collect()).
    """
    if _MODELS.get("whisper") is not None:
        del _MODELS["whisper"]
        _MODELS["whisper"] = None
        gc.collect()
        logger.info("Whisper GPU'dan boşaltıldı (unload).")
        _log_gpu_memory("whisper unload sonrası")


def _ensure_whisper_loaded():
    """GPU modunda whisper istekten önce boşaltılmış olabilir -- burada gerekirse yeniden yükler."""
    if _MODELS.get("whisper") is None:
        logger.info("Whisper yeniden yükleniyor...")
        _MODELS["whisper"] = _load_whisper()
    return _MODELS["whisper"]


def _load_ner():
    from transformers import (
        AutoModelForTokenClassification,
        AutoTokenizer,
        pipeline,
    )

    model_name = "savasy/bert-base-turkish-ner-cased"
    tokenizer = AutoTokenizer.from_pretrained(model_name)
    model = AutoModelForTokenClassification.from_pretrained(model_name)

    ner_pipeline = pipeline(
        "ner",
        model=model,
        tokenizer=tokenizer,
        aggregation_strategy="simple",
        device=-1,  # CPU -> VRAM'i whisper'a bırakıyoruz
    )
    logger.info("Türkçe NER modeli yüklendi (%s, cpu)", model_name)
    return ner_pipeline


def _load_diarization():
    """
    pyannote/speaker-diarization-3.1 pipeline'ını yükler.

    DIARIZATION_DEVICE=cpu ise doğrudan CPU'ya sabitlenir (eski davranış).
    DIARIZATION_DEVICE=gpu ise başlangıçta yine CPU'da tutulur -- GPU'ya
    taşınması, whisper'ın o an bellekte olmadığından emin olunduktan sonra,
    her istekte transcribe() içinde DİNAMİK olarak yapılır.

    HF_TOKEN ortam değişkeni yoksa ya da model indirilemezse (ör. kullanım
    şartları henüz kabul edilmemişse) diarization'ı SESSİZCE devre dışı
    bırakır -- servis whisper+NER ile normal çalışmaya devam eder, sadece
    yanıtlarda konuşmacı etiketi olmaz.
    """
    hf_token = os.environ.get("HF_TOKEN")
    if not hf_token:
        logger.warning(
            "HF_TOKEN ortam değişkeni bulunamadı -- speaker diarization "
            "DEVRE DIŞI kalacak. Kurulum talimatlarındaki HuggingFace token "
            "adımını tamamlayıp HF_TOKEN'ı ayarladıktan sonra servisi "
            "yeniden başlat."
        )
        return None

    try:
        import torch
        from pyannote.audio import Pipeline

        # TF32 (TensorFloat-32): SADECE GPU modunda açılıyor. RTX 3060 (Ampere
        # mimarisi) bunu donanımsal destekliyor -- matris çarpımlarını FP32'den
        # biraz daha düşük hassasiyetle ama belirgin daha hızlı yapar. Bu
        # GERÇEK bir ödünleşim (yüzde 0 kalite kaybı değil) ama pratikte
        # diarization sonucunu (kim ne zaman konuştu) neredeyse hiç
        # etkilemiyor, endüstride yaygın kullanılan bir hızlandırma. Bilerek
        # sadece GPU modunda ve açıkça yorumla belgeleyerek açıyoruz.
        if _DIARIZATION_DEVICE == "gpu":
            torch.backends.cuda.matmul.allow_tf32 = True
            torch.backends.cudnn.allow_tf32 = True
            logger.info("TF32 hızlandırması açıldı (sadece GPU modunda, küçük bir hassasiyet ödünleşimi var).")

        # CPU thread sayısını fiziksel çekirdek sayısına açıkça ayarlıyoruz
        # -- PyTorch bazen varsayılan olarak daha düşük bir sayı seçebiliyor.
        # Bu, MODELİ hiç değiştirmez, sadece CPU'yu ne kadar paralel
        # kullandığını etkiler -- doğruluk kaybı YOK, sadece hız kazancı.
        cpu_count = os.cpu_count() or 4
        torch.set_num_threads(cpu_count)
        logger.info("Torch CPU thread sayısı %d olarak ayarlandı.", cpu_count)

        pipeline_obj = Pipeline.from_pretrained(
            "pyannote/speaker-diarization-3.1",
            token=hf_token,
        )
        pipeline_obj.to(torch.device("cpu"))

        # Batch boyutlarını artırmak, aynı hesaplamayı (aynı model, aynı
        # sonuç) CPU'da daha verimli/vektörize şekilde yapmasını sağlar --
        # çıktı BİREBİR AYNI kalır, sadece daha hızlı üretilir. Ortam
        # değişkeniyle ayarlanabilir, çok yüksek bir değer RAM kullanımını
        # artırabileceği için 16 ile başlıyoruz.
        seg_batch = int(os.environ.get("DIARIZATION_SEGMENTATION_BATCH_SIZE", "16"))
        emb_batch = int(os.environ.get("DIARIZATION_EMBEDDING_BATCH_SIZE", "16"))
        try:
            pipeline_obj.segmentation_batch_size = seg_batch
            pipeline_obj.embedding_batch_size = emb_batch
            logger.info(
                "Diarization batch boyutları ayarlandı: segmentation=%d, embedding=%d",
                seg_batch, emb_batch,
            )
        except Exception:
            logger.warning(
                "Diarization batch boyutları ayarlanamadı (bu pyannote sürümü "
                "desteklemiyor olabilir) -- varsayılan (daha yavaş) davranışla devam ediliyor."
            )

        logger.info(
            "Pyannote speaker diarization pipeline yüklendi (başlangıç: cpu, mod: %s)",
            _DIARIZATION_DEVICE,
        )
        return pipeline_obj
    except Exception:
        logger.exception(
            "Speaker diarization pipeline yüklenemedi -- diarization DEVRE "
            "DIŞI kalacak. HF_TOKEN doğru mu ve "
            "pyannote/speaker-diarization-3.1 + pyannote/segmentation-3.0 "
            "için HuggingFace'te kullanım şartlarını kabul ettin mi kontrol et."
        )
        return None


@asynccontextmanager
async def lifespan(app: FastAPI):
    _MODELS["ner"] = _load_ner()
    _MODELS["diarization"] = _load_diarization()
    if _DIARIZATION_DEVICE == "cpu":
        # Eski, kararlı davranış: whisper kalıcı yüklü, hiç boşaltılmaz.
        _MODELS["whisper"] = _load_whisper()
    else:
        # GPU modu: whisper İLK İSTEK gelince yüklenecek (istek başına
        # yükle/boşalt döngüsünün bir parçası).
        _MODELS["whisper"] = None
        logger.info(
            "DIARIZATION_DEVICE=gpu -- whisper başlangıçta yüklenmeyecek, "
            "ilk istekte dinamik olarak yüklenecek."
        )
    yield
    _MODELS.clear()


app = FastAPI(title="Lokal Deşifre ve Sansürleme Motoru", lifespan=lifespan)

# --------------------------------------------------------------------------
# 2) İSTEK / YANIT ŞEMALARI
# --------------------------------------------------------------------------


class TranscribeRequest(BaseModel):
    job_id: str = Field(
        default="",
        description="C# tarafındaki TranscriptionJob.Id -- ilerleme bildirimlerini bu job'a yazmak için kullanılır.",
    )
    audio_path: str = Field(..., description="Sunucunun erişebildiği yerel ses dosyası yolu")
    language: str = Field(default="tr")
    censor_labels: list[str] = Field(
        default_factory=lambda: ["PER", "LOC"],
        description="Sansürlenecek NER etiketleri (PER=kişi, LOC=yer, ORG=kurum)",
    )

    # Konuşmacı ayrımı isteğe bağlı -- False ise pyannote HİÇ ÇALIŞMAZ,
    # (uzun kayıtlarda en büyük süre kaynağı budur), sadece whisper ile
    # deşifre yapılır. Varsayılan True -- eski davranışla geriye dönük
    # uyumlu (C# tarafı her zaman açıkça gönderiyor olacak zaten).
    diarization: bool = Field(
        default=True,
        description="False ise speaker diarization tamamen atlanır (whisper-only, çok daha hızlı).",
    )

    # Speaker diarization ayarları -- hepsi opsiyonel, boş bırakılırsa
    # pyannote konuşmacı sayısını kendisi tahmin eder.
    num_speakers: Optional[int] = Field(
        default=None, description="Konuşmacı sayısı biliniyorsa (ör. 2), doğruluğu artırır."
    )
    min_speakers: Optional[int] = Field(default=None)
    max_speakers: Optional[int] = Field(default=None)

    chunk_seconds: float = Field(
        default=600.0,
        description=(
            "Whisper'a gönderilecek her parçanın HEDEF süresi (saniye). "
            "Gerçek kesim noktası bu hedefe en yakın SESSİZLİK anına denk "
            "getirilir (kelime/cümle ortasından asla kesilmez). Dosya zaten "
            "bu süreden kısaysa hiç parçalama yapılmaz."
        ),
    )


class Segment(BaseModel):
    start: float
    end: float
    speaker: Optional[str] = None  # ör. "Konuşmacı 1" -- diarization devre dışıysa None
    text: str
    text_censored: str


class TranscribeResponse(BaseModel):
    status: Literal["ok"]
    full_text: str
    full_text_censored: str
    segments: list[Segment]
    detected_entities: list[dict]
    diarization_enabled: bool


# --------------------------------------------------------------------------
# 3) SANSÜRLEME MANTIĞI (değişmedi)
# --------------------------------------------------------------------------

_WHITESPACE_RE = re.compile(r"\s+")


@dataclass
class EntitySpan:
    start: int
    end: int
    label: str


def _find_entities(text: str, ner_pipeline, target_labels: set[str]) -> list[EntitySpan]:
    if not text.strip():
        return []

    raw_entities = ner_pipeline(text)
    spans: list[EntitySpan] = []

    for ent in raw_entities:
        label = ent["entity_group"].upper()
        if label not in target_labels:
            continue
        spans.append(EntitySpan(start=int(ent["start"]), end=int(ent["end"]), label=label))

    spans.sort(key=lambda s: s.start)
    merged: list[EntitySpan] = []
    for span in spans:
        if merged and span.start <= merged[-1].end:
            merged[-1] = EntitySpan(
                start=merged[-1].start,
                end=max(merged[-1].end, span.end),
                label=merged[-1].label,
            )
        else:
            merged.append(span)
    return merged


def censor_text(text: str, ner_pipeline, target_labels: set[str]) -> tuple[str, list[dict]]:
    spans = _find_entities(text, ner_pipeline, target_labels)
    if not spans:
        return text, []

    result_chars = list(text)
    detected = []
    for span in reversed(spans):
        original = text[span.start:span.end]
        detected.append({"text": original, "label": span.label, "start": span.start, "end": span.end})
        result_chars[span.start:span.end] = list("[GİZLENDİ]")

    return "".join(result_chars), list(reversed(detected))


# --------------------------------------------------------------------------
# 3.5) UZUN KAYITLAR İÇİN PARÇALAMA (VAD/sessizlik tabanlı, GPU'ya sürekli
#      1-2 saatlik kesintisiz yük bindirmemek için)
# --------------------------------------------------------------------------
#
# NEDEN: Uzun (1-2 saatlik) tek bir whisper çağrısı, GPU sürücüsünü
# kesintisiz saatlerce maksimum yükte tutuyor. Gerçek bir sistemde bu,
# sürücü seviyesinde bir çökmeye (Windows Bug Check 0xD1) yol açtı. Çözüm,
# sesi ayrı, kısa whisper çağrılarına bölmek -- her çağrı arasında GPU'nun
# "nefes alması" için doğal bir boşluk oluşuyor.
#
# KELİME ORTASINDAN KESMEME GARANTİSİ: Kesim noktaları asla rastgele/
# kronometreyle seçilmiyor -- ffmpeg'in silencedetect filtresiyle tespit
# edilen gerçek sessizlik anlarına denk getiriliyor. Sessizlik tespiti
# başarısız olursa (ör. sürekli konuşulan, hiç boşluk olmayan bir kayıt),
# GÜVENLİ TARAFTA kalıp parçalamayı tamamen atlıyoruz -- kör zaman
# kesmesi yapmıyoruz.


def _get_audio_duration(audio_path: str) -> float:
    ffprobe_exe = os.environ.get("FFPROBE_EXE", "ffprobe")
    result = subprocess.run(
        [
            ffprobe_exe, "-v", "error", "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1", audio_path,
        ],
        capture_output=True, text=True, timeout=30, check=True,
    )
    return float(result.stdout.strip())


def _detect_silences(audio_path: str, noise_db: str = "-30dB", min_duration: float = 0.6) -> list[tuple[float, float]]:
    """ffmpeg'in silencedetect filtresiyle (start, end) sessizlik aralıklarını bulur."""
    ffmpeg_exe = os.environ.get("FFMPEG_EXE", "ffmpeg")
    result = subprocess.run(
        [ffmpeg_exe, "-i", audio_path, "-af", f"silencedetect=noise={noise_db}:d={min_duration}", "-f", "null", "-"],
        capture_output=True, text=True, timeout=300,
    )

    silences: list[tuple[float, float]] = []
    pending_start: Optional[float] = None
    for line in result.stderr.splitlines():
        if "silence_start" in line:
            try:
                pending_start = float(line.split("silence_start:")[1].strip())
            except (IndexError, ValueError):
                pending_start = None
        elif "silence_end" in line and pending_start is not None:
            try:
                end_str = line.split("silence_end:")[1].split("|")[0].strip()
                silences.append((pending_start, float(end_str)))
            except (IndexError, ValueError):
                pass
            pending_start = None
    return silences


def _build_chunk_boundaries(
    total_duration: float, silences: list[tuple[float, float]], target_seconds: float
) -> list[tuple[float, float]]:
    """Hedef süreye en yakın sessizlik noktalarından (start, end) parça sınırları üretir."""
    if total_duration <= target_seconds * 1.3:
        return [(0.0, total_duration)]

    boundaries: list[tuple[float, float]] = []
    chunk_start = 0.0
    next_target = target_seconds

    for s_start, s_end in silences:
        midpoint = (s_start + s_end) / 2
        if midpoint >= next_target and midpoint > chunk_start:
            boundaries.append((chunk_start, midpoint))
            chunk_start = midpoint
            next_target = chunk_start + target_seconds

    if chunk_start < total_duration:
        boundaries.append((chunk_start, total_duration))

    return boundaries if len(boundaries) > 1 else [(0.0, total_duration)]


def _plan_chunks(audio_path: str, target_seconds: float) -> list[tuple[float, float]]:
    """
    Parçalama planını çıkarır. Herhangi bir adımda (süre/sessizlik tespiti)
    hata olursa GÜVENLİ TARAFTA kalıp tek parça (parçalamasız) döner --
    parçalama mekanizmasının kendisi asla yeni bir hata kaynağı olmamalı.
    """
    try:
        total_duration = _get_audio_duration(audio_path)
    except Exception:
        logger.exception("Ses süresi alınamadı, dosya tek parça olarak işlenecek.")
        return [(0.0, 0.0)]  # 0.0 => "tam dosyayı kullan" anlamına gelir, bkz. transcribe()

    if total_duration <= target_seconds * 1.3:
        return [(0.0, total_duration)]

    try:
        silences = _detect_silences(audio_path)
    except Exception:
        logger.exception(
            "Sessizlik tespiti başarısız oldu -- kelime ortasından kesme "
            "riskini almamak için parçalama ATLANACAK, dosya tek parça "
            "olarak işlenecek."
        )
        return [(0.0, total_duration)]

    return _build_chunk_boundaries(total_duration, silences, target_seconds)


def _extract_chunk(audio_path: str, start: float, end: float, chunk_dir: Path) -> Path:
    ffmpeg_exe = os.environ.get("FFMPEG_EXE", "ffmpeg")
    chunk_path = chunk_dir / f"chunk_{start:.2f}_{end:.2f}.wav"
    subprocess.run(
        [ffmpeg_exe, "-y", "-i", audio_path, "-ss", str(start), "-to", str(end), "-c", "copy", str(chunk_path)],
        capture_output=True, timeout=120, check=True,
    )
    return chunk_path


# --------------------------------------------------------------------------
# 4) SPEAKER DIARIZATION -- segmentlere konuşmacı ataması
# --------------------------------------------------------------------------


def _extract_annotation(diarization_result):
    """
    pyannote.audio sürümüne göre pipeline çağrısının döndürdüğü nesne
    değişebiliyor: eski sürümlerde doğrudan bir Annotation (itertracks()
    metoduna sahip), yeni sürümlerde ise bunu saran bir DiarizeOutput
    nesnesi dönebiliyor. İkisini de destekliyoruz.
    """
    if hasattr(diarization_result, "itertracks"):
        return diarization_result

    for attr_name in ("speaker_diarization", "exclusive_speaker_diarization", "annotation", "diarization"):
        candidate = getattr(diarization_result, attr_name, None)
        if candidate is not None and hasattr(candidate, "itertracks"):
            return candidate

    raise RuntimeError(
        f"Beklenmeyen diarization çıktı tipi: {type(diarization_result)} -- "
        f"mevcut alanlar: {[a for a in dir(diarization_result) if not a.startswith('_')]}"
    )


def _run_diarization(
    diarization_pipeline, audio_path: str, req: TranscribeRequest
) -> list[tuple[float, float, str]]:
    """Pyannote pipeline'ını çalıştırır, (start, end, ham_etiket) turlarını döner."""
    kwargs: dict = {}
    if req.num_speakers:
        kwargs["num_speakers"] = req.num_speakers
    else:
        if req.min_speakers:
            kwargs["min_speakers"] = req.min_speakers
        if req.max_speakers:
            kwargs["max_speakers"] = req.max_speakers

    diarization_result = diarization_pipeline(audio_path, **kwargs)
    annotation = _extract_annotation(diarization_result)

    turns = [
        (turn.start, turn.end, label)
        for turn, _, label in annotation.itertracks(yield_label=True)
    ]
    turns.sort(key=lambda t: t[0])
    return turns


class _SpeakerNamer:
    """Ham pyannote etiketlerini (SPEAKER_00 vb.) ilk konuşma sırasına göre
    'Konuşmacı 1', 'Konuşmacı 2' şeklinde insan-okunur isimlere çevirir."""

    def __init__(self) -> None:
        self._label_to_name: dict[str, str] = {}

    def get_name(self, raw_label: str) -> str:
        if raw_label not in self._label_to_name:
            self._label_to_name[raw_label] = f"Konuşmacı {len(self._label_to_name) + 1}"
        return self._label_to_name[raw_label]


def _assign_speaker(
    seg_start: float, seg_end: float, turns: list[tuple[float, float, str]], namer: _SpeakerNamer
) -> Optional[str]:
    """Bir whisper segmentini, zaman aralığı en çok örtüşen diarization
    turuna (yani konuşmacıya) eşler."""
    best_overlap = 0.0
    best_label: Optional[str] = None

    for turn_start, turn_end, label in turns:
        overlap = min(seg_end, turn_end) - max(seg_start, turn_start)
        if overlap > best_overlap:
            best_overlap = overlap
            best_label = label

    return namer.get_name(best_label) if best_label is not None else None


# --------------------------------------------------------------------------
# 5) ANA UÇTAN UCA AKIŞ: SES -> METİN -> KONUŞMACI -> SANSÜRLENMİŞ METİN
# --------------------------------------------------------------------------


@app.post("/transcribe", response_model=TranscribeResponse)
def transcribe(req: TranscribeRequest):
    audio_path = Path(req.audio_path)
    if not audio_path.exists():
        raise HTTPException(status_code=404, detail=f"Dosya bulunamadı: {audio_path}")

    ner_pipeline = _MODELS["ner"]
    diarization_pipeline = _MODELS.get("diarization")
    target_labels = {lbl.upper() for lbl in req.censor_labels}

    logger.info("Transkripsiyon başladı: %s", audio_path.name)
    _report_progress(req.job_id, 5, "Başlatılıyor")

    # Kilidi burada, gerçek işlem başlamadan HEMEN önce alıyoruz. Aynı anda
    # başka bir istek işleniyorsa, bu istek burada BEKLER -- kuyruğa
    # alınmış gibi davranır, hata vermez, sadece sırasını bekler. Böylece
    # kaç HTTP isteği aynı anda gelirse gelsin, GPU/model erişimi HER ZAMAN
    # tek seferde, sırayla gerçekleşir.
    logger.info("İşlem sırası bekleniyor (varsa aktif başka bir işlem bitene kadar): %s", audio_path.name)
    with _PROCESSING_LOCK:
        logger.info("İşlem sırası geldi, başlanıyor: %s", audio_path.name)
        # GPU modunda whisper bir önceki istekten sonra boşaltılmış olabilir --
        # burada gerekirse yeniden yükleniyor. CPU modunda zaten hep resident.
        whisper_model = _ensure_whisper_loaded()
        _log_gpu_memory("istek öncesi")
        _log_system_memory("istek öncesi")

        chunk_dir = Path(tempfile.mkdtemp(prefix="whisper_chunks_"))
        try:
            chunk_boundaries = _plan_chunks(str(audio_path), req.chunk_seconds)
            multi_chunk = len(chunk_boundaries) > 1

            if multi_chunk:
                logger.info(
                    "Uzun kayıt tespit edildi: %d parçaya bölünecek (hedef ~%.0fs/parça, "
                    "sessizlik noktalarından kesiliyor).",
                    len(chunk_boundaries), req.chunk_seconds,
                )
            else:
                logger.info("Dosya tek parça olarak işlenecek (parçalama gerekmiyor).")

            raw_segments: list[tuple[float, float, str]] = []
            previous_tail_text = ""

            # --- HIZ OPTİMİZASYONU: diarization'ı whisper ile PARALEL başlat ---
            # Diarization (CPU) ve whisper (GPU) birbirinden tamamen bağımsız
            # donanım kullanıyor -- birinin diğerini beklemesi için hiçbir
            # teknik sebep yok. Diarization'ı burada, whisper döngüsü daha
            # BAŞLAMADAN arka plan thread'inde tetikliyoruz; whisper GPU'da
            # parça parça çalışırken diarization CPU'da paralel ilerliyor.
            # Whisper bitince diarization ya zaten bitmiş olur (bekleme
            # SIFIR) ya da kalan kısmı için kısa bir süre beklenir -- hiçbir
            # doğruluk kaybı yok, sadece boşa geçen sıra bekleme süresi
            # ortadan kalkıyor.
            diarization_enabled = diarization_pipeline is not None and req.diarization
            if diarization_pipeline is not None and not req.diarization:
                logger.info(
                    "Konuşmacı ayrımı bu istek için KAPALI (diarization=false) -- "
                    "pyannote hiç çalıştırılmayacak, sadece whisper ile deşifre yapılacak."
                )
            diarization_future = None
            diarization_executor: Optional[ThreadPoolExecutor] = None

            if diarization_enabled and _DIARIZATION_DEVICE == "cpu":
                # Sadece CPU modunda paralel başlatıyoruz -- GPU modunda
                # whisper ve diarization ASLA aynı anda GPU'da olmamalı,
                # bu yüzden diarization'ı whisper tamamen bitip GPU'dan
                # boşaltıldıktan SONRA, aşağıda sırayla başlatıyoruz.
                logger.info("Speaker diarization ARKA PLANDA (CPU, whisper ile paralel) başladı: %s", audio_path.name)
                diarization_start_time = time.perf_counter()
                diarization_executor = ThreadPoolExecutor(max_workers=1)
                diarization_future = diarization_executor.submit(
                    _run_diarization, diarization_pipeline, str(audio_path), req
                )

            for i, (c_start, c_end) in enumerate(chunk_boundaries):
                if multi_chunk:
                    logger.info(
                        "Parça %d/%d işleniyor (%.1fs - %.1fs)",
                        i + 1, len(chunk_boundaries), c_start, c_end,
                    )
                    _log_gpu_memory(f"parça {i + 1}/{len(chunk_boundaries)} öncesi")
                    chunk_path = _extract_chunk(str(audio_path), c_start, c_end, chunk_dir)
                else:
                    chunk_path = audio_path

                segments_iter, info = whisper_model.transcribe(
                    str(chunk_path),
                    language=req.language,
                    beam_size=5,
                    # initial_prompt: bir önceki parçanın son cümlelerini bu
                    # parçaya bağlam olarak veriyoruz -- whisper'ın kendi resmi
                    # mekanizması, parçalar arası cümle bütünlüğünü korumak için.
                    initial_prompt=previous_tail_text or None,
                    # vad_filter=True: Silero VAD ile konuşma bölgelerini tespit
                    # edip sadece onları işler, sessizlikleri atlar.
                    vad_filter=True,
                    vad_parameters={
                        "min_silence_duration_ms": 500,
                        # speech_pad_ms: her konuşma bölgesinin başına/sonuna
                        # eklenen tampon süre -- kelime kırpılma riskini pratikte
                        # sıfıra indirir. faster-whisper varsayılanı zaten
                        # 400ms'dir, burada bilinçli olarak açıkça belirtiyoruz.
                        "speech_pad_ms": 400,
                    },
                )

                chunk_segments = [
                    (seg.start + c_start, seg.end + c_start, seg.text.strip())
                    for seg in segments_iter
                ]
                raw_segments.extend(chunk_segments)

                if chunk_segments:
                    previous_tail_text = " ".join(s[2] for s in chunk_segments[-3:])[-200:]

                if multi_chunk:
                    chunk_path.unlink(missing_ok=True)
                    gc.collect()
                    _log_gpu_memory(f"parça {i + 1}/{len(chunk_boundaries)} sonrası")

                # İlerleme: whisper toplam çubuğun %10-%70'ini kaplar (diarization
                # açıksa, sonrasına yer bırakmak için), kapalıysa %10-%90'ını.
                whisper_upper_bound = 70 if diarization_enabled else 90
                whisper_progress = 10 + int((i + 1) / len(chunk_boundaries) * (whisper_upper_bound - 10))
                _report_progress(
                    req.job_id, whisper_progress,
                    f"Parça {i + 1}/{len(chunk_boundaries)} işlendi" if multi_chunk else "Ses işleniyor",
                )

            turns: list[tuple[float, float, str]] = []
            namer = _SpeakerNamer()

            if diarization_enabled and _DIARIZATION_DEVICE == "cpu":
                _report_progress(req.job_id, 75, "Konuşmacı ayrımı tamamlanıyor")
                logger.info("Whisper bitti, arka plandaki diarization sonucu bekleniyor (varsa)...")
                wait_start_time = time.perf_counter()
                try:
                    turns = diarization_future.result()
                    total_diarization_time = time.perf_counter() - diarization_start_time
                    extra_wait_time = time.perf_counter() - wait_start_time
                    logger.info(
                        "Speaker diarization bitti: %d konuşma turu, TOPLAM %.1f saniye sürdü "
                        "(whisper zaten bitmişti, %.1f saniye ek bekleme oldu -- bu sayı 0'a "
                        "yakınsa paralelleştirme diarization'ı tamamen gizlemiş demektir; büyükse "
                        "diarization whisper'dan daha uzun sürüyor ve GPU'ya taşımak gerçekten "
                        "zaman kazandırır).",
                        len(turns), total_diarization_time, max(extra_wait_time, 0.0),
                    )
                except Exception:
                    logger.exception(
                        "Arka plan diarization başarısız oldu -- konuşmacı etiketi eklenmeyecek, "
                        "transkripsiyon yine de tamamlanacak."
                    )
                    diarization_enabled = False
                finally:
                    diarization_executor.shutdown(wait=False)

            elif diarization_enabled and _DIARIZATION_DEVICE == "gpu":
                # --- SIRALI GPU MODU ---
                # Whisper ve diarization ASLA aynı anda VRAM'de resident+aktif
                # olmuyor: önce whisper'ı GERÇEKTEN bellekten sil (unload),
                # sonra diarization'ı GPU'ya taşı ve çalıştır, sonra onu da
                # CPU'ya geri taşıyıp VRAM'i serbest bırak, en son whisper'ı
                # bir sonraki istek için yeniden yükle.
                import torch  # DIARIZATION_DEVICE=gpu ise CUDA'lı torch kurulu olmalı

                logger.info("Whisper bitti. Sıralı GPU modu: whisper boşaltılıyor...")
                _report_progress(req.job_id, 75, "Konuşmacı ayrımı hazırlanıyor (GPU)")
                _unload_whisper()

                logger.info("Diarization pipeline GPU'ya taşınıyor...")
                _transfer_start = time.perf_counter()
                diarization_pipeline.to(torch.device("cuda"))
                torch.cuda.synchronize()  # transfer'in GERÇEKTEN bitmesini bekle (asenkron olabilir)
                _transfer_time = time.perf_counter() - _transfer_start
                logger.info("[ZAMANLAMA] GPU'ya taşıma: %.2f saniye", _transfer_time)
                _log_gpu_memory("diarization GPU'ya taşındıktan sonra")

                try:
                    diarization_start_time = time.perf_counter()
                    turns = _run_diarization(diarization_pipeline, str(audio_path), req)
                    total_diarization_time = time.perf_counter() - diarization_start_time
                    logger.info(
                        "[ZAMANLAMA] Gerçek diarization hesaplaması: %.2f saniye "
                        "(bu, transfer HARİÇ, sadece _run_diarization() çağrısı)",
                        total_diarization_time,
                    )
                    logger.info(
                        "Speaker diarization (GPU) bitti: %d konuşma turu, "
                        "toplam %.1f saniye (transfer=%.1fs + hesaplama=%.1fs).",
                        len(turns), _transfer_time + total_diarization_time, _transfer_time, total_diarization_time,
                    )
                except Exception:
                    logger.exception("GPU diarization başarısız oldu -- konuşmacı etiketi eklenmeyecek.")
                    diarization_enabled = False
                finally:
                    _transfer_back_start = time.perf_counter()
                    logger.info("Diarization pipeline CPU'ya geri taşınıyor, VRAM serbest bırakılıyor...")
                    diarization_pipeline.to(torch.device("cpu"))
                    torch.cuda.empty_cache()
                    logger.info("[ZAMANLAMA] CPU'ya geri taşıma: %.2f saniye", time.perf_counter() - _transfer_back_start)
                    _log_gpu_memory("diarization CPU'ya geri taşındıktan sonra")

                    # Bir sonraki isteğin whisper'ı hazır bulması için hemen
                    # yeniden yüklüyoruz (bu isteğin süresine ekleniyor ama
                    # sıradaki isteği hızlandırıyor).
                    logger.info("Whisper bir sonraki istek için yeniden yükleniyor...")
                    _MODELS["whisper"] = _load_whisper()

            else:
                logger.info("Speaker diarization devre dışı, konuşmacı etiketi eklenmeyecek.")

            _report_progress(req.job_id, 92, "Kişisel veriler sansürleniyor")

            segments_out: list[Segment] = []
            full_text_parts: list[str] = []
            full_censored_parts: list[str] = []
            all_entities: list[dict] = []

            for seg_start, seg_end, seg_text in raw_segments:
                # NER sansürleme, konuşmacı etiketi eklenmeden ÖNCE ham segment
                # metnine uygulanıyor -- bu sayede detected_entities'teki start/end
                # ofsetleri her zaman orijinal metne göre kalıyor, "[Konuşmacı 1]: "
                # önekinden etkilenmiyor.
                censored, entities = censor_text(seg_text, ner_pipeline, target_labels)

                speaker_name = (
                    _assign_speaker(seg_start, seg_end, turns, namer) if diarization_enabled else None
                )

                if speaker_name:
                    display_text = f"[{speaker_name}]: {seg_text}"
                    display_censored = f"[{speaker_name}]: {censored}"
                else:
                    display_text = seg_text
                    display_censored = censored

                segments_out.append(
                    Segment(
                        start=seg_start,
                        end=seg_end,
                        speaker=speaker_name,
                        text=display_text,
                        text_censored=display_censored,
                    )
                )
                full_text_parts.append(display_text)
                full_censored_parts.append(display_censored)
                all_entities.extend(entities)

            # NOT: 'info' artık sadece SON parçaya ait -- toplam süre için
            # chunk_boundaries'in son sınırını kullanıyoruz, tek bir parçanın
            # süresini değil.
            total_audio_duration = chunk_boundaries[-1][1] if chunk_boundaries else 0.0
            logger.info(
                "Transkripsiyon bitti: %s (dil=%s, toplam süre=%.1fs, parça=%d, entity=%d, diarization=%s)",
                audio_path.name, req.language, total_audio_duration, len(chunk_boundaries),
                len(all_entities), diarization_enabled,
            )
            _report_progress(req.job_id, 99, "SRT hazırlanıyor")

            return TranscribeResponse(
                status="ok",
                # Konuşmacı etiketli satırlar zaten okunabilir bir diyalog formatı
                # oluşturduğu için "\n" ile birleştiriyoruz (eskiden " " idi).
                full_text="\n".join(full_text_parts),
                full_text_censored="\n".join(full_censored_parts),
                segments=segments_out,
                detected_entities=all_entities,
                diarization_enabled=diarization_enabled,
            )
        finally:
            # Başarılı bitse de hata fırlatsa da HER ZAMAN çalışır. Büyük ara
            # nesneler (ses feature'ları, segment listeleri, NER/diarization
            # tensor'ları) burada scope dışına çıkmadan önce Python'un çöp
            # toplayıcısını elle tetikliyoruz -- 2 saatlik kayıtlarda oluşan
            # büyük nesnelerin bellekte gereksiz beklemesini önler. Bu GERÇEK
            # bir etkisi olan tek şey; torch.cuda.empty_cache() (yukarıdaki
            # açıklamaya bakın) burada anlamsız olurdu.
            shutil.rmtree(chunk_dir, ignore_errors=True)
            gc.collect()
            _log_gpu_memory("istek sonrası (cleanup sonrası)")
            _log_system_memory("istek sonrası (cleanup sonrası)")


@app.get("/health")
def health():
    return {
        "status": "ok",
        "models_loaded": list(_MODELS.keys()),
        "diarization_enabled": _MODELS.get("diarization") is not None,
    }


if __name__ == "__main__":
    # Bu blok, sadece dosya DOĞRUDAN çalıştırıldığında devreye giriyor --
    # geliştirme sırasında hep "uvicorn transcribe_censor_service:app ..."
    # komutuyla başlatıyorduk, o zaman uvicorn'un KENDİ CLI'ı sunucuyu
    # ayağa kaldırıyordu, bu blok hiç çalışmıyordu. Ama PyInstaller ile
    # paketlenmiş .exe çift tıklandığında (ya da doğrudan çalıştırıldığında)
    # dışarıda bizi başlatacak bir "uvicorn komutu" YOK -- kendi kendimizi
    # başlatmamız gerekiyor.
    import uvicorn

    host = os.environ.get("HOST", "127.0.0.1")
    port = int(os.environ.get("PORT", "8500"))

    logger.info("Doğrudan .exe olarak başlatılıyor: %s:%s", host, port)
    uvicorn.run(app, host=host, port=port, workers=1)
