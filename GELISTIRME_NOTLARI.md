# TitanGameHub - Geliştirme Notları

Bu klasör, taşınabilir sürümün kaynak ve çalıştırma dosyalarını içerir.

## Derleme (Windows)

### 1) Hazır betik (önerilen, mevcut yöntem)
- `compile.bat` : .NET Framework 4.x `csc.exe` ile `bin\TitanGameHub.exe` üretir.
- `run.bat`     : Derlenmiş EXE'yi çalıştırır.
- `build_portable.bat` : Taşınabilir paket derlemesi/çıktısı için proje betiği.

> Not: `compile.bat`, `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` ve `res\webview2\` altındaki WebView2 DLL'lerini kullanır.

### 2) SDK-style proje dosyası (ek)
- `TitanGameHub.csproj` eklendi (`net48`, WPF + WinForms + WebView2 referansları).
- Visual Studio 2022 veya uygun .NET Framework targeting pack yüklü bir ortamda `dotnet build TitanGameHub.csproj` / MSBuild ile açılıp derlenebilir.
- Ortama göre referans/derleme farkları yaşanırsa kaynak doğruluk için `compile.bat` esas alınmalıdır.

## Yapı
- `src/` : Uygulama C# kaynak kodları (UI, kurulum/indirme, katalog, servisler).
- `bin/` : Çalıştırılabilir dosya, veritabanı (`games_v6.cfg`), araçlar ve loglar.
- `res/` : İkon ve WebView2 bileşenleri.
- `tools/` : Yardımcı araçlar (örn. katalog worker betiği).
- `KURULUM_VE_BASLAT.bat`, `baslat.bat`, `KUR.ps1` : Kurulum/başlatma akışları.

## Temizlik
- Bu çalışma sırasında iç içe geçmiş eski arşivler (`TitanGameHub_Portable_FinalFix.zip`, `TitanGameHub_Portable_Fix.zip`) temizlenmiş ayrı bir paket üretildi: masaüstündeki `TitanGameHub_Portable_temiz.zip`. Orijinal paket korunmuştur.
