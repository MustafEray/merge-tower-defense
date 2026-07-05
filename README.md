# Merge Tower Defense

Sürükle-bırak ile aynı seviyedeki kuleleri birleştirerek (merge) savunma
yapılan, F# + Fable ile yazılan bir tower defense oyunu.

Mimari, faz planı ve kodlama kuralları için [CLAUDE.md](CLAUDE.md) dosyasına
bakın. Şu an **Aşama 1 (Çekirdek Durum Motoru)** tamamlanmış durumdadır:
`src/Shared.fs` ve `src/State.fs` saf oyun mantığını içerir; render (PixiJS)
ve mobil paketleme sonraki fazlardadır.

## Çalıştırma

```bash
npm install
npm test   # F# kodunu Fable ile JS'e derler ve testleri node ile koşar
```

`npm test`, dotnet SDK gerektirmeyen `fable-compiler-js` derleyicisini
kullanır. dotnet SDK'lı ortamlarda çekirdek ayrıca
`dotnet build src/MergeTowerDefense.fsproj` ile derlenebilir.
