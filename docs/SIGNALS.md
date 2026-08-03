# SIGNALS.md — Standard mô tả signal (Kat34Scalper)

Chuẩn mô tả cho MỌI signal trong indicator. Mỗi signal là một sub-module độc lập
(`src/Kat34Scalper.AlertSignal.AX.cs` hoặc `src/Kat34Scalper.Signal.BX.cs`) và PHẢI được mô tả ở đây.

## Quy ước chung (áp dụng cho mọi signal)

- **Alert Signal (A1, A2...) vs Bot Signal (B1, B2...)**:
  - **ALERT SIGNAL**: Tạo âm thanh cảnh báo (Alert Sound) và vẽ hình/đường giá trên chart (chỉ vẽ), **không** tham gia chạy Bot hay bắn order.
  - **BOT SIGNAL**: Quản lý tín hiệu chạy Bot khi HUD BOT ON, vẽ đường Entry/SL/TP và gửi order sang Bot execution. Format vẽ nhãn chart: `Buy B1`, `Sell B1`, `Buy B2`, `Sell B2`.
- **Default OFF.** Mỗi signal có công tắc `Enabled` (default `false`) trong settings group riêng và toggle trên HUD.
- **History Days.** Mỗi signal có `HistoryDays` (default `3`). Khi bật ON, sub-module TỰ ĐỘNG tính toán lại và vẽ trên chart trong cửa sổ N ngày gần nhất.
- **OFF = độc lập tuyệt đối.** Tắt signal chỉ xoá drawing có tag prefix của chính nó (`K34S_ALERTA1_`, `K34S_B1_`...).
- **Ownership rule:** Mỗi signal sở hữu prefix vẽ riêng. Clear button HUD xóa TOÀN BỘ K34S_* + K8934_*.
- Mọi signal chạy trên primary series của chart (`Calculate.OnBarClose`).

---

## ALERT SIGNALS (A1, A2...)

### A1 — Alert Signal A1 (Placeholder)
- File: `src/Kat34Scalper.AlertSignal.A1.cs` | Settings group: `2. Alert Signal A1` | HUD toggle: ALERT SIGNAL › `A1`
- Mục đích: Cảnh báo tín hiệu âm thanh và vẽ marker/đường giá trên chart cho người dùng theo dõi. KHÔNG bắn lệnh Bot.

### A2 — Alert Signal A2 (Placeholder)
- File: `src/Kat34Scalper.AlertSignal.A2.cs` | Settings group: `2.5 Alert Signal A2` | HUD toggle: ALERT SIGNAL › `A2`
- Mục đích: Cảnh báo tín hiệu âm thanh và vẽ marker/đường giá trên chart cho người dùng theo dõi. KHÔNG bắn lệnh Bot.

---

## BOT SIGNALS (B1, B2...)

## B1 — 34bounce8+ (34+8+Bounce)

- File: `src/Kat34Scalper.Signal.B1.cs` | Settings group: `3. Bot Signal B1 — 34bounce8+` | HUD toggle: BOT SIGNAL › `B1 (34bounce8+)`
- Mục đích: bắt nhịp bounce từ EMA 34 trong trend stack mạnh (BUY 34bounce8+: EMAs xếp chồng 8>=34>89>144>200) — pullback chạm EMA 34 rồi bật lên, vào lệnh stop ở đỉnh nến chạm. Format nhãn: `Buy B1` / `Sell B1`.
- Điều kiện nền (trend stack) — BUY (SELL mirror ngược lại), **mỗi điều kiện có toggle riêng trong settings**:
  1. `Cond: EMA 8 above EMA 34` — EMA 8 nằm trên HOẶC touch EMA 34 (không được cross down). Touch cho phép (`>=`).
  2. `Cond: EMA 34 above EMA 89` — strict `>`.
  3. `Cond: EMA 89 above EMA 144` — strict `>`.
  4. `Cond: EMA 144 above EMA 200` — strict `>`.
  Mất stack → hủy entry đang pending.
- Settings: `B1Enabled` (false), `B1HistoryDays` (3), 4 cond toggles (true), `B1EntryOffsetTicks` (1), `B1StopDistanceTicks` (60, fallback), `B1TargetDistanceTicks` (120, fallback).
- Tag drawing: signal lines `K34S_B1_<B/S>_<suffix>_<bar>`; text label `K34S_B1_TX_<B/S>_<bar>`.
- Filter ảnh hưởng: Global Filter (`PassFilters`: ADX, Volume, Time window, MTF).
- Nhãn chart text: `Buy B1` (dưới nến) / `Sell B1` (trên nến).

### Bảng giai đoạn (states) — BUY minh họa, SELL mirror

| State | Phase logic | Điều kiện VÀO | Điều kiện RA / hành động |
|-------|-------------|----------------|---------------------------|
| idle | `Active=false` | trend stack hợp lệ, chưa có touch | nến touch (wick low <= EMA34) + close TRÊN EMA34 → NewEntry |
| pending | `Active=true`, `RefExtreme` = high nến touch | NewEntry: pending stop LONG ở `high + Entry Offset`, vẽ lines + text `Buy B1` | touch sau có high THẤP hơn → Migrate; high chạm trigger → Filled; close < EMA34 hoặc mất stack → Cancel |

---

## B2 — 89uturn34 (89-34 Pullback)

- File: `src/Kat34Scalper.Signal.B2.cs` | Settings group: `3.5 Bot Signal B2 — 89uturn34` | HUD toggle: BOT SIGNAL › `B2 (89uturn34)`
- Mục đích: bắt pullback về EMA 89 trong trend EMA 34/89 rồi đảo chiều tiếp tục trend (rejection). Format nhãn: `Buy B2` / `Sell B2`.
- Điều kiện nền (trend): Sell = EMA fast(34) < EMA slow(89); Buy = fast > slow. Mất trend → reset sequence về idle.
- Settings: `B2Enabled` (false), `B2HistoryDays` (3), `EmaFastPeriod` (34), `EmaSlowPeriod` (89), `MaxSequenceBars` (30), `B2EntryOffsetTicks` (1), `B2StopDistanceTicks` (60, fallback), `B2TargetDistanceTicks` (120, fallback).
- Tag drawing: stage markers `K34S_B2ST_<B/S>_<bar>`; signal lines/labels `K34S_B2_<B/S>_<suffix>_<bar>`.
- Filter ảnh hưởng: Global Filter (`PassFilters`: ADX, Volume, Time window, MTF).
- Nhãn chart text: `Buy B2` / `Sell B2`.

### Bảng giai đoạn (stages) — Sell minh hoạ, Buy mirror

| Stage | Marker | Phase | Điều kiện VÀO | Điều kiện RA / hành động |
|-------|--------|-------|----------------|---------------------------|
| idle | — | 0 | Trend hợp lệ, chưa có setup | close < EMA34 → vào arm |
| arm | `B2-arm` | 1 | close vượt xuống DƯỚI EMA34 | close > EMA34 → vào pull (đếm SeqBars=1) |
| pull | `B2-pull` | 2 | close cross ngược qua EMA34 về EMA89, chưa chạm 89 | high ≥ EMA89 → pull-T |
| pull-T | `B2-pull-T` | 2 | pullback chạm/vượt EMA89 | close < EMA34 (U-turn) → fire `Buy B2` / `Sell B2` |
