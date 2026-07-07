# Merge Tower Defense

Sürükle-bırak ile aynı seviyedeki kuleleri birleştirerek (merge) savunma
yapılan, F# + Fable ile yazılan bir tower defense oyunu.

Mimari, faz planı ve kodlama kuralları için [CLAUDE.md](CLAUDE.md) dosyasına
bakın. **Aşama 1 (Çekirdek Durum Motoru)**, **Aşama 2 (Render ve Girdi)**,
**Aşama 3 (Oyun Sistemleri: dalgalar, yol, savaş, ekonomi)** ve **Aşama 4
(Cila: animasyon, ses, HUD/UX, denge)** tamamlandı: saf F# çekirdeği
(`src/Shared.fs`, `src/State.fs`, `src/Ui.fs`) PixiJS v7 canvas'ı ve React
HUD'u ile sürülüyor, kuleler gerçek sanat (bkz. `public/towers/`) kullanıyor.
**Aşama 5 (Mobil Paketleme)** sürüyor: Capacitor entegrasyonu ve dokunmatik
optimizasyon tamamlandı, `android/` native projesi depoda.

## Çalıştırma

```bash
npm install
npm test             # saf çekirdek testleri (dotnet SDK gerektirmez)
npm run dev          # oyunu Vite dev sunucusunda aç
npm run build        # üretim paketi (dist/)
npm run verify:e2e   # headless Chromium ile uçtan uca doğrulama
```

Derleme, dotnet SDK gerektirmeyen `fable-compiler-js` ile yapılır. dotnet
SDK'lı ortamlarda çekirdek ayrıca `dotnet build src/MergeTowerDefense.fsproj`
ile derlenebilir.

## Mobil (Capacitor)

```bash
npm run cap:sync       # üretim paketini derler ve android/ ile senkronlar
npm run android:open   # Android Studio'yu android/ projesiyle açar
```

Android Studio + Android SDK kurulu bir makine gerektirir — bu geliştirme
ortamında yalnızca scaffold/sync akışı doğrulanabildi.
