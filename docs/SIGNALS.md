# SIGNALS.md — Standard mô tả signal (Kat34Scalper)

Chuẩn mô tả cho MỌI signal trong indicator. Mỗi signal là một sub-module độc lập
(`src/Kat34Scalper.Signal.AX.cs`) và PHẢI được mô tả ở đây theo đúng template dưới đây
trước/khi code. Khi thêm signal mới (A2, A3...): copy template, điền đủ mọi mục.

## Quy ước chung (áp dụng cho mọi signal)

- **Default OFF.** Mỗi signal có `AXEnabled` (default `false`) trong settings group riêng
  (`N. Signal AX — Tên`) và toggle riêng trên HUD (section SIGNAL).
- **History Days.** Mỗi signal có `AXHistoryDays` (default `3`). Khi bật ON (settings hoặc
  HUD), sub-module TỰ ĐỘNG tính toán lại và vẽ trên chart trong cửa sổ N ngày gần nhất
  (backfill một lần, không alert sound, không bắn order bot). Sau backfill, state của
  state machine được đồng bộ sang live để không bắt đầu lại từ idle.
- **OFF = độc lập tuyệt đối.** Tắt signal chỉ xoá drawing có tag prefix của chính nó,
  reset state machine của nó; không ảnh hưởng signal khác.
- **Ownership rule (MANDATORY cho tất cả signal hiện tại và tương lai):** Mỗi signal là một sub-module độc lập. Khi HUD toggle ON: sub-module backfill + vẽ toàn bộ drawings của nó (signal lines/entry/SL/TP + stage markers + arrow/text nếu có). Khi OFF: chỉ xoá NHỮNG GÌ CHÍNH NÓ VẼ RA (dùng prefix `K34S_<OWNER>_` và `K34S_<OWNER>ST_`), không đụng đến các signal khác. ON lại: vẽ lại chính nó. OFF lại: chỉ xóa của nó. Clear button HUD xóa TOÀN BỘ K34S_* + K8934_* của HUD này (không phân biệt owner).
- **Stage marker** là text label persistent vẽ tại bar xảy ra giai đoạn (không phải marker
  trôi theo bar hiện tại), tag unique per bar.
- Mọi signal chạy trên primary series của chart (`Calculate.OnBarClose`).

## Template chuẩn cho một signal

```
## AX — <tên signal>
- File: src/Kat34Scalper.Signal.AX.cs | Settings group: "N. Signal AX — <tên>" | HUD toggle: SIGNAL › <label>
- Mục đích: <1 câu — signal này bắt cái gì>
- Điều kiện nền (trend/context): <điều kiện bắt buộc trước khi sequence chạy>
- Settings: <liệt kê + default>
- Tag drawing: <prefix>
- Filter ảnh hưởng: <filter nào gate signal này, theo hướng nào>

### Bảng giai đoạn (stages)
| Stage | Marker | Phase | Điều kiện VÀO | Điều kiện RA / hành động |
|-------|--------|-------|----------------|---------------------------|
| ...   | ...    | ...   | ...            | ...                       |

### Signal fire
- Điều kiện fire: <chính xác bar nào, close hay wick>
- Entry: <quy tắc giá entry> | SL: <...> | TP: <...>
- Bot: <hành vi bot khi fire>
```

---

## A0 — EMA-ribbon Fan

- File: `src/Kat34Scalper.Signal.A0.cs` | Settings group: `2. Signal A0 — EMA Fan` | HUD toggle: SIGNAL › `A0 fan`
- Mục đích: phát hiện lúc ribbon EMA "xoè quạt" — trend đủ mạnh và đang giãn ra.
- Điều kiện nền: đủ dữ liệu warmup (EMA 200 + `Fan Spread Lookback` bar).
- Settings: `Enabled` (false), `History Days` (3). Tham số fan dùng chung với filter:
  `Fan Min Spread (ticks)` (20), `Fan Spread Lookback (bars)` (5) trong group `1. Filters`.
- Tag drawing: `K34S_A0_<bar>`
- Filter ảnh hưởng: KHÔNG. A0 là signal độc lập; direction của nó (-1/0/+1) được Filter module
  dùng làm gate cho A1 khi `A0 Fan Filter Enabled` bật — nhưng không filter nào gate A0.

### Bảng giai đoạn (stages)

| Stage | Marker | Phase | Điều kiện VÀO | Điều kiện RA / hành động |
|-------|--------|-------|----------------|---------------------------|
| idle | — | 0 | Ribbon KHÔNG fanned (EMAs không theo thứ tự strict, hoặc không giãn, hoặc spread < min) | — |
| fanned (buy) | ▲ DodgerBlue dưới low | +1 | EMA 9>21>34>55>89>144>200 strict, spread(9↔200) > spread `Fan Spread Lookback` bars trước, spread ≥ `Fan Min Spread (ticks)` | Bar ĐẦU của episode: vẽ ▲ + alert sound (chỉ live). Episode kết thúc khi fan collapse → re-arm |
| fanned (sell) | ▼ OrangeRed trên high | -1 | EMA 9<21<34<55<89<144<200 strict + giãn + đủ rộng | Bar ĐẦU của episode: vẽ ▼ + alert sound (chỉ live) |

### Signal fire
- Điều kiện fire: bar đầu tiên ribbon chuyển từ "không fan" sang "fan" (hoặc đổi hướng fan), close basis.
- Entry/SL/TP: KHÔNG vẽ (A0 chỉ là marker cảnh báo trend). Bot không trade A0.
- Bot: không.

---

## A1 — 89/34 Pullback

- File: `src/Kat34Scalper.Signal.A1.cs` | Settings group: `3. Signal A1 — 89/34 Pullback` | HUD toggle: SIGNAL › `A1 89-34`
- Mục đích: bắt pullback về EMA 89 trong trend EMA 34/89 rồi đảo chiều tiếp tục trend (rejection).
- Điều kiện nền (trend): Sell = EMA fast(34) < EMA slow(89); Buy = fast > slow. Mất trend → reset sequence về idle. Mọi bước tính trên CLOSE basis (wick không tính cross), trừ touch slow EMA dùng high/low.
- Settings: `Enabled` (false), `History Days` (3), `Fast EMA Period` (34), `Slow EMA Period` (89), `Max Sequence Bars` (30), `Trigger Mode` (Breakdown), `Entry Offset (ticks)` (1), `Stop Distance (ticks)` (60, fallback), `Target Distance (ticks)` (120, fallback).
- Tag drawing: stage markers `K34S_A1ST_<B/S>_<bar>`; signal (entry/SL/TP/arrow/label) `K34S_<B/S>_<suffix>_<bar>`.
- Filter ảnh hưởng: group `1. Filters` — A0 fan gate (session-only), MTF, ADX, Volume, Time window. Filter gate VIỆC PHÁT signal (emission), KHÔNG gate tiến trình sequence (state machine vẫn chạy khi trend còn).

### Bảng giai đoạn (stages) — Sell minh hoạ, Buy mirror

| Stage | Marker | Phase | Điều kiện VÀO | Điều kiện RA / hành động |
|-------|--------|-------|----------------|---------------------------|
| idle | — | 0 | Trend hợp lệ, chưa có setup | close < EMA34 → vào arm |
| arm | `A1-arm` | 1 | close vượt xuống DƯỚI EMA34 (pullback bắt đầu từ phía dưới) | close > EMA34 → vào pull (bắt đầu đếm SeqBars=1) |
| pull | `A1-pull` | 2 | close cross ngược lên qua EMA34 hướng về EMA89, CHƯA chạm EMA89 | high ≥ EMA89 → pull-T; close < EMA34 trước khi chạm 89 → FAILED, về arm (không marker) |
| pull-T | `A1-pull-T` | 2 | pullback đã touch/cross EMA89 (wick tính) | close < EMA34 (U-turn) → fire (Breakdown) hoặc vào U (RetestBounce) |
| U-turn wait | `A1-U` | 3 | U-turn close xuống lại qua EMA34 sau khi đã pull-T (chỉ RetestBounce) | close > EMA34 (retest) → fire; quá `Max Sequence Bars` → expire về idle |
| expired | — | 0 | SeqBars > `Max Sequence Bars` (đếm từ bar cross) | reset, re-arm từ đầu |

### Signal fire
- **Breakdown** (default, 4 bước): fire NGAY tại bar U-turn close qua EMA34 (sau pull-T).
- **Retest Bounce** (5 bước): sau U-turn, fire tại bar close ngược lại qua EMA34 (retest).
- Entry: stop ở candidate tốt hơn trong 2 candidate — C1 = low/high của bar U-turn, C2 = extreme tốt hơn sau đó (sell: low cao hơn / buy: high thấp hơn, bar vẫn close đúng phía EMA34); sell = `max(C1,C2) - Entry Offset`, buy = `min(C1,C2) + Entry Offset`.
- SL/TP: từ ATM template (`mnq. 1ct...` default: SL 60 / TP 120) nếu template định nghĩa, fallback `Stop/Target Distance`.
- Bot: nếu BOT ON + `Bot Enabled` → submit stop (hoặc limit nếu giá đã chạy qua) qua ATM template trên account `Sim101` (default). Backfill/replay KHÔNG bắn order.

### Drawing khi fire
- Entry line solid (per-side color), SL dashed red, TP dashed green; C1/C2 faded dotted nếu khác nhau; ATM trigger lines BE (DeepSkyBlue dash-dot) / SL1 (orange dot) / SL2 (magenta dot) khi template có.
- Lines hiển thị tối đa `Line Length (bars)` (7).
- **Ownership prefix:** mọi drawing của signal A1 dùng prefix `K34S_A1_<B/S>_` (entry/SL/TP) và `K34S_A1ST_<B/S>_` (stage markers). Khi A1 OFF chỉ xóa prefix này. Clear HUD xóa toàn bộ `K34S_*`.

---

## A2 — 34+8+Bounce

- File: `src/Kat34Scalper.Signal.A2.cs` | Settings group: `3.5 Signal A2 — 34+8+Bounce` | HUD toggle: SIGNAL › `A2 34+8`
- Mục đích: bắt nhịp bounce từ EMA 34 trong trend stack mạnh (BUY 34+++: EMAs xếp chồng 8>34>89>144>200) — pullback chạm EMA 34 rồi bật lên, vào lệnh stop ở đỉnh nến chạm.
- Điều kiện nền (trend stack) — BUY (SELL mirror ngược lại), **mỗi điều kiện có toggle riêng trong settings**:
  1. `Cond: EMA 8 above EMA 34` — EMA 8 nằm trên HOẶC touch EMA 34 (không được cross down). Touch cho phép (`>=`).
  2. `Cond: EMA 34 above EMA 89` — strict `>`.
  3. `Cond: EMA 89 above EMA 144` — strict `>`.
  4. `Cond: EMA 144 above EMA 200` — strict `>`.
  Mất stack (bất kỳ điều kiện nào đang bật fail) → hủy entry đang pending. SELL: 8<=34 (touch OK, không cross up), 34<89, 89<144, 144<200.
- Settings: `Enabled` (false), `History Days` (3), 4 cond toggles (true), `Entry Offset (ticks)` (1), `Stop Distance (ticks)` (60, fallback), `Target Distance (ticks)` (120, fallback).
- Tag drawing: signal lines `K34S_A2_<B/S>_<suffix>_<bar>`; text label `K34S_A2_TX_<B/S>_<bar>`.
- Filter ảnh hưởng: KHÔNG (tính trên chính TF chart). Filter TF lớn hơn sẽ add sau ở section Filter.
- **KHÔNG có stage markers** — signal một giai đoạn duy nhất, chỉ vẽ entry/SL/TP lines + text `Buy A2` (dưới nến, màu Buy Text) / `Sell A2` (trên nến, màu Sell Text) tại nến entry.

### Bảng giai đoạn (states) — BUY minh họa, SELL mirror

| State | Phase logic | Điều kiện VÀO | Điều kiện RA / hành động |
|-------|-------------|----------------|---------------------------|
| idle | `Active=false` | trend stack hợp lệ, chưa có touch | nến touch (wick low <= EMA34) + close TRÊN EMA34 → NewEntry |
| pending | `Active=true`, `RefExtreme` = high nến touch | NewEntry: pending stop LONG ở `high + Entry Offset`, vẽ lines + text | touch sau có high THẤP hơn → Migrate (dời entry xuống); high chạm trigger → Filled; close < EMA34 hoặc mất stack → Cancel |

- Thứ tự check mỗi bar: **Filled trước** (high >= `RefExtreme + Entry Offset`) → **Cancel** (close < EMA34 hoặc `!trendOk`) → **touch**: chưa active → NewEntry, high < RefExtreme → Migrate, ngược lại None.
- Nến touch mà close DƯỚI EMA34: không bao giờ có entry (touch phải close trên mới đặt pending stop Buy).
- Đỉnh nến touch sau CAO hơn entry hiện tại: không làm gì — stop Buy đã khớp trước đó (logic Filled cover khi high >= trigger).

### Signal fire
- Điều kiện fire: bar touch đầu tiên (wick chạm EMA34, close đúng phía) — close basis (OnBarClose).
- Entry: stop ở extreme nến touch ± `Entry Offset` (BUY: high + offset / SELL: low − offset); Migrate dời theo extreme tốt hơn.
- SL/TP: từ ATM template nếu có, fallback `Stop/Target Distance` của group A2.
- Bot: BOT ON + `Bot Enabled` → submit stop (limit nếu giá đã chạy qua) với **offset của A2**; A2 Cancel hủy pending order thuộc owner `A2` (không đụng order của A1). Backfill/replay không bắn order.

### Drawing
- Entry line solid (per-side color), SL dashed red, TP dashed green, ATM trigger lines khi template có — chuẩn chung.
- Text `Buy A2` dưới low nến entry (Buy Text Color) / `Sell A2` trên high (Sell Text Color); khi Migrate text dời sang nến entry mới.
- Cancel → xóa lines + text của setup đó. Filled → lines fade theo `Line Length`, text ở lại nến entry.
- **Ownership prefix:** `K34S_A2_`. A2 OFF chỉ xóa prefix này + records owner `A2`. Clear HUD xóa toàn bộ `K34S_*`.
