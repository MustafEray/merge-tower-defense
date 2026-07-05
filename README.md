# Merge Tower Defense

Sürükle-bırak ile aynı seviyedeki kuleleri birleştirerek (merge) savunma
yapılan, F# + Fable ile yazılan bir tower defense oyunu.

Mimari, faz planı ve kodlama kuralları için [CLAUDE.md](CLAUDE.md) dosyasına
bakın. **Aşama 1 (Çekirdek Durum Motoru)**, **Aşama 2 (Render ve Girdi)** ve
**Aşama 3 (Oyun Sistemleri: dalgalar, yol, savaş, ekonomi)** tamamlandı: saf
F# çekirdeği (`src/Shared.fs`, `src/State.fs`, `src/Ui.fs`) PixiJS v7
canvas'ı ve React HUD'u ile sürülüyor. Cila (Aşama 4) ve mobil paketleme
(Aşama 5) sonraki fazlardadır.

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
