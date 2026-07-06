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
  çevrilmesi, grid/kule/düşman çizimi (tamamen prosedürel, asset yok —
  Aşama 4'te yalnızca kule görselleri için bilinçli olarak gözden geçirildi,
  aşağıya bakın), sürükleme hayaleti (drag ghost), menzil görselleştirme,
  drop önizleme vurguları, React HUD (altın/dalga/satın alma — altın ve
  dalga değerleri Aşama 3 ekonomisine kadar UI katmanında placeholder).
- Ticker `deltaMS` → `DeltaTime` enjeksiyonu: hareket kare hızından
  bağımsızdır. Demo düşman üreteci (`Ui.demoSpawnPeriod`) Aşama 3'teki
  dalga planlayıcının iskele kodudur.

### Aşama 3 — Oyun Sistemleri (Gün 8–10) ✅ TAMAMLANDI

- Dalga planlayıcı (`WavePhase`: BetweenWaves → Spawning → WaveActive;
  spawn kredisi sayesinde tick granülaritesinden bağımsız), yol geometrisi
  (`Path`: hücre biriminde polyline; varsayılan rota üst kenar + sağ kanat),
  kule hedefleme/atış döngüsü ("first" hedefleme, cooldown, menzil), ekonomi
  (`Gold`/`Lives` doğrulanmış tipleri, kule fiyat artışı, bounty + dalga
  bonusu) ve zorluk eğrisi (`Waves` modülü: kompozisyon, can çarpanı,
  spawn aralığı). Oyun sonu: `GameStatus = Playing of Lives | Defeated`.
- Aşama 2'nin UI placeholder'ları (altın/dalga) ve demo düşman üreteci
  kaldırıldı; ekonomi ve dalgalar artık çekirdekte.

### Aşama 4 — Cila (Gün 11–12)

- Animasyon: `Ui.Effect` (`KillBurst`/`MergeFlash`/`SpawnPop`) ve can kaybı
  ekran flaşı (`Ui.LifeFlash`) — `Shots` ile aynı fade-then-expire deseni.
- Ses: `Ui.SoundCue` + `View/Sound.fs` (prosedürel Web Audio tonları, asset
  yok) ve HUD'da ses açma/kapama düğmesi (`Ui.Muted`).
- UI/UX: düşük can HUD uyarısı (`Ui.isLowLives`).
- Oyun dengesi: Frost'a gerçek yavaşlatma (chill) etkisi (`Enemy.Slow`,
  `slowSpeedFactor`/`slowDurationSeconds`), Cannon'a gerçek sıçrama (splash)
  hasarı (`TowerStats.SplashRadius`, `State.resolveHit`) — ikisi de öncesinde
  Archer'dan strictly dominate ediliyordu.
- **Asset istisnası (kule görselleri):** proje sahibinin açık isteğiyle
  kule sprite'ları için "tamamen prosedürel, asset yok" kuralı bilinçli
  olarak esnetildi. `public/towers/{archer,cannon,frost}.png` — Kenney'in
  CC0 lisanslı "Tower Defense" paketinden (bkz.
  `public/towers/KENNEY-LICENSE.txt`) seçilmiş üç görsel, Vite tarafından
  olduğu gibi `dist/`'e kopyalanır. `Interop/Pixi.fs`'e `Texture`/`Sprite`
  binding'leri eklendi; `View/Render.fs` kuleleri artık `Graphics` yerine
  `Sprite` ile çiziyor (seviyeye göre boyut hâlâ `towerDisplayHeight`,
  seviyeye göre ton hâlâ mevcut `towerShade` dizileriyle — `Sprite.tint`
  üzerinden gerçek sanata uygulanıyor). Düşmanlar, grid, yol, efektler ve
  menzil/önizleme örtüleri hâlâ tamamen prosedürel — istisna yalnızca kule
  görselleriyle sınırlı.

### Aşama 5 — Mobil Paketleme (Gün 13–14)

- Capacitor entegrasyonu, dokunmatik optimizasyon, performans profili,
  sürüm çıkışı.

## Kod Haritası

| Dosya | İçerik |
|---|---|
| `src/Shared.fs` | Domain tipleri: kimlikler, `TowerLevel`, `TowerType` (Cannon = `SplashRadius`'lu alan hasarı, Frost = `slowSpeedFactor`/`slowDurationSeconds`'lı yavaşlatma), `Tower`, `GridSize`, `Coord`, `Grid`, `Health`, `Damage`, `Gold`, `Lives`, `DeltaTime`, `PathProgress`, `Path`, `EnemyType`, `Enemy` (`Slow` sayacı dahil) ve saf yardımcıları |
| `src/State.fs` | Durum makinesi: `Interaction`, `WavePhase`/`WaveState`, `GameStatus`, `Waves` (zorluk eğrisi), `GameState`, `Msg`, `GameEvent`, `RejectReason`, `previewDrop`, tick hattı (dalga→hareket/can→savaş [hedef + Cannon sıçraması `resolveHit` ile birleşik çözülür]→dalga sonu), `update` |
| `src/Ui.fs` | Saf UI katmanı: `Layout` (path sınırlarından türetilen canvas geometrisi + hit test), `UiModel` (hover/ghost/notice/atış izleri/`Effect` patlamaları/`LifeFlash`/tek seferlik `SoundCue` kuyruğu/`Muted` tercihi), `UiMsg`, `isLowLives`, `updateUi` |
| `src/Interop/Pixi.fs` | Minimal el yazımı PixiJS v7 binding'leri: `Graphics` (prosedürel çizim yüzeyi) + `Texture`/`Sprite` (kule görselleri için, `loadTexture`/`createSprite`) |
| `src/Interop/React.fs` | Minimal React 18 binding'leri + Feliz-vari HTML DSL |
| `src/Interop/Dom.fs` | Üç DOM dokunuşu (getElementById/appendChild/globalThis) |
| `src/Interop/Audio.fs` | Minimal el yazımı Web Audio API binding'leri (AudioContext + osilatör/gain zarfı ile prosedürel ton üretimi, asset yok) |
| `src/View/Render.fs` | Grid, yol, düşmanlar, menzil daireleri, drop önizleme, patlama efektleri, can kaybı flaşı hâlâ prosedürel (`Graphics`); kuleler + sürükleme hayaleti artık gerçek sanat (`Sprite`, bkz. `public/towers/`), seviye boyut/ton/pip mantığı korundu |
| `public/towers/` | Kule sprite'ları (Kenney CC0 "Tower Defense" paketinden, bkz. `KENNEY-LICENSE.txt`) — Vite tarafından olduğu gibi `dist/`'e kopyalınır |
| `src/View/Hud.fs` | React HUD: altın/dalga/düşman sayacı (düşük can uyarı stiliyle), satın alma butonu, ses açma/kapama düğmesi, bildirim satırı |
| `src/View/Sound.fs` | `Ui.SoundCue` değerlerini prosedürel Web Audio tonlarına eşler (AudioContext'i uygulama ömrü boyunca elinde tutar) |
| `src/App.fs` | Kompozisyon kökü (tek impure modül): Pixi app, ticker→`Frame dt`, pointer→`Msg`, mini-MVU döngüsü, her dispatch sonrası ses kuyruğunun çalınması, e2e debug kancası |
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
