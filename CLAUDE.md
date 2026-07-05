# Merge Tower Defense — Proje Mimarisi ve 14 Günlük Yol Haritası

## Proje Özeti

NxN'lik bir grid üzerinde, sürükle-bırak (drag & drop) ile aynı tip ve aynı
seviyedeki iki kuleyi birleştirerek (merge) daha güçlü kuleler üreten ve yol
boyunca ilerleyen düşman dalgalarına karşı savunma yapılan bir tower defense
oyunu.

Teknoloji yığını:

- **F#** — tüm oyun mantığı
- **Fable** — F# → JavaScript derleyicisi (bu ortamda dotnet'siz
  `fable-compiler-js`; dotnet erişilebilir olduğunda Fable 4/5 CLI)
- **MVU döngüsü** — tek yönlü veri akışı. Nuget erişimi olmadığı için
  Aşama 2'de Elmish yerine `App.fs` içinde el yazımı mini-MVU kuruldu;
  Elmish'e geçiş mekanik bir değişikliktir.
- **PixiJS v7** — render motoru (Aşama 2'de eklendi; binding'ler
  `src/Interop/Pixi.fs` içinde elle yazıldı)
- **React 18** — HUD katmanı (Feliz nuget'i yerine `src/Interop/React.fs`
  içinde minimal el yazımı binding + Feliz-vari DSL)
- **Capacitor** — mobil paketleme (Aşama 5'te eklenecek)

## Mimari İlkeler (tüm fazlar için bağlayıcı)

1. **Saf çekirdek:** `src/Shared.fs` ve `src/State.fs` hiçbir zaman DOM,
   PixiJS, IO, rastgelelik veya duvar saati bilmez. Zaman (`DeltaTime`) ve
   rastgelelik daima dışarıdan parametre olarak enjekte edilir.
2. **Illegal states unrepresentable:** Doğrulanmış değerler yalnızca smart
   constructor'lardan üretilir (`private` DU/record + `tryCreate`). Geçersiz
   bir değerin tipi yoktur; dolayısıyla var olamaz.
3. **Değişmezlik (immutability):** Mutation yok. Her oyun geçişi
   `update : Msg -> GameState -> GameState * GameEvent list` saf fonksiyonundan
   geçer. (Tek istisna: test koşucusundaki sayaçlar.)
4. **Tek yönlü veri akışı:** UI yalnızca `Msg` gönderir ve dönen
   `GameEvent`'leri görselleştirir. UI, `GameState`'i asla doğrudan değiştirmez.
5. **Exception politikası:** `failwith` yalnızca tip sistemiyle dışlanamayan
   ama modül API'si gereği kanıtlanabilir şekilde erişilemez (unreachable)
   invariant ihlalleri için kullanılır ve nedeni yorumla belgelenir.

## 14 Günlük Plan ve Faz Sınırları

Faz atlamak yasaktır: bir sonraki faza, kullanıcı (proje sahibi) onayı
olmadan geçilmez.

### Aşama 1 — Çekirdek Durum Motoru (Gün 1–3) ✅ TAMAMLANDI

- Kapsam: domain modeli (grid, kule tipleri/seviyeleri, düşmanlar),
  drag & drop + merge durum makinesi, boş/dolu hücre yönetimi, saf birim
  testleri.
- Kapsam DIŞI: render, gerçek input, dalga zamanlaması, ekonomi, hedefleme.

### Aşama 2 — Render ve Girdi (Gün 4–7) ✅ TAMAMLANDI

- PixiJS binding'leri, MVU döngüsü, pointer/touch olaylarının `Msg`'e
  çevrilmesi, grid/kule/düşman çizimi (tamamen prosedürel, asset yok),
  sürükleme hayaleti (drag ghost), menzil görselleştirme, drop önizleme
  vurguları, React HUD (altın/dalga/satın alma — altın ve dalga değerleri
  Aşama 3 ekonomisine kadar UI katmanında placeholder).
- Ticker `deltaMS` → `DeltaTime` enjeksiyonu: hareket kare hızından
  bağımsızdır. Demo düşman üreteci (`Ui.demoSpawnPeriod`) Aşama 3'teki
  dalga planlayıcının iskele kodudur.

### Aşama 3 — Oyun Sistemleri (Gün 8–10)

- Dalga planlayıcı (wave scheduler), yol (path) geometrisi, kule hedefleme ve
  atış döngüsü, ekonomi (altın/can), zorluk eğrisi.

### Aşama 4 — Cila (Gün 11–12)

- Animasyon, ses, UI/UX iyileştirmeleri, oyun dengesi ayarları.

### Aşama 5 — Mobil Paketleme (Gün 13–14)

- Capacitor entegrasyonu, dokunmatik optimizasyon, performans profili,
  sürüm çıkışı.

## Kod Haritası

| Dosya | İçerik |
|---|---|
| `src/Shared.fs` | Domain tipleri: kimlikler, `TowerLevel`, `TowerType`, `Tower`, `GridSize`, `Coord`, `Grid`, `Health`, `Damage`, `DeltaTime`, `PathProgress`, `EnemyType`, `Enemy` ve saf yardımcıları |
| `src/State.fs` | Durum makinesi: `Interaction` (Idle/Dragging), `GameState`, `Msg`, `GameEvent`, `RejectReason`, `previewDrop`, `update` |
| `src/Ui.fs` | Saf UI katmanı: `Layout` (canvas geometrisi + hit test), `UiModel`, `UiMsg`, `updateUi`, HUD placeholder'ları, demo düşman üreteci |
| `src/Interop/Pixi.fs` | Minimal el yazımı PixiJS v7 binding'leri (yalnızca kullanılan yüzey) |
| `src/Interop/React.fs` | Minimal React 18 binding'leri + Feliz-vari HTML DSL |
| `src/Interop/Dom.fs` | Üç DOM dokunuşu (getElementById/appendChild/globalThis) |
| `src/View/Render.fs` | Prosedürel çizim: grid, kuleler (tip=şekil, seviye=boy/ton/pip), düşmanlar, menzil daireleri, drop önizleme, drag ghost |
| `src/View/Hud.fs` | React HUD: altın/dalga/düşman sayacı, satın alma butonu, bildirim satırı |
| `src/App.fs` | Kompozisyon kökü (tek impure modül): Pixi app, ticker→`Frame dt`, pointer→`Msg`, mini-MVU döngüsü, e2e debug kancası |
| `src/MergeTowerDefense.fsproj` | Uygulama projesi (Fable ile derlenir) |
| `tests/Tests.fs` | Bağımlılıksız mini test koşucusu ile saf birim testleri (core + Ui) |
| `tests/Tests.fsproj` | Test projesi (saf dosyalar + testler; interop dosyaları dahil edilmez) |
| `index.html` | Vite giriş noktası; `#hud-root` (React) ve `#game-root` (Pixi) izole kökler |
| `scripts/verify-e2e.mjs` | Headless Chromium ile uçtan uca doğrulama (satın alma, drag-merge, döngü) |

## Komutlar

- `npm install` — bağımlılıkları kurar
- `npm test` — `fable-compiler-js` ile F# kodunu JS'e derler ve testleri
  node ile koşar. **dotnet SDK gerektirmez** (kısıtlı ağ ortamları için).
- `npm run dev` — uygulamayı derler ve Vite dev sunucusunda açar
- `npm run build` — üretim paketi (`dist/`)
- `npm run verify:e2e` — üretim paketini headless Chromium'da uçtan uca
  doğrular (ekran görüntüleri `out/e2e/` altına düşer)
- dotnet SDK mevcut ortamlarda: `dotnet build src/MergeTowerDefense.fsproj`
  ve Aşama 2'den itibaren `dotnet fable` (Fable 4/5 CLI).

Not: `fable-standalone` paketindeki bir paketleme hatası (UMD bundle +
`"type": "module"`) nedeniyle `scripts/patch-fable-toolchain.mjs` postinstall
adımında tek satırlık bir import düzeltmesi uygular. dotnet tabanlı Fable
CLI'a geçildiğinde bu betik kaldırılabilir.

## Kodlama Kuralları

- Yeni doğrulanmış tip = `private` temsil + `tryCreate` + değer okuyucu.
- DU'larda anlamlı, alan adlı case'ler tercih edilir.
- Çekirdek dosyalara UI/IO importu eklemek yasaktır.
- Her yeni durum geçişi için `tests/Tests.fs`'e senaryo testi eklenir.
- Kod ve yorumlar İngilizce; dokümantasyon Türkçe olabilir.
