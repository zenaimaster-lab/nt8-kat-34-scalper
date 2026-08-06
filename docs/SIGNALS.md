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

### A1 — Alert Signal A1 (EmaZone30s)
- File: `../nt8-kat-A1-TradeBackground/Kat34Scalper.AlertSignal.A1.cs` (independent sibling repo) | Settings group: `2. Alert Signal A1 — EmaZone30s` | HUD toggle: ALERT SIGNAL › `A1 (EmaZone30s)`
- Mục đích: Cảnh báo môi trường trend "fan" (EMA xếp quạt + góc nghiêng EMA 34) bằng vertical line + âm thanh. KHÔNG bắn lệnh Bot. **Độc lập hoàn toàn** với Bot Signals: series riêng, EMA riêng, không dùng chung state/drawing/logic.
- Series riêng: `AddDataSeries(Second, AlertA1PeriodSeconds)` (default 30s) — chạy đúng TF này dù chart TF bất kỳ. EMA 8/34/144/200 tính riêng trên `BarsArray[1]`.
- Điều kiện môi trường — LONG (SHORT mirror ngược lại), **mỗi điều kiện có toggle riêng**:
  1. `Cond: EMA 8 above EMA 34` — strict `>`.
  2. `Cond: EMA 34 above EMA 89` — strict `>` (xác nhận trend, chặn spike EMA nhanh trong thị trường chưa xác nhận).
  3. `Cond: EMA 89 above EMA 144` — strict `>`.
  4. `Cond: EMA 144 above EMA 200` — strict `>`.
  5. `Cond: EMA 34 slope angle` (`AlertA1AngleEnabled`) — góc nghiêng EMA 34 tối thiểu `+Min Angle` độ (lên) cho LONG; tối thiểu `-Min Angle` độ (xuống) cho SHORT. Góc chuẩn hoá tự động bằng ATR trên series 30s: `atan(Δema34/bar / ATR(period))` — 45° = slope 1 ATR/bar, không phụ thuộc zoom, không cần chỉnh tay theo instrument (`ATR Period` chỉnh được). **Default OFF** (property đổi tên ở v0.67 để xoá giá trị `true` lưu cũ) — slope bar 30s thường quá nhỏ so với ATR nên 30° hiếm khi đạt; bật tay khi muốn dùng.
  6. `Cond: EMA34 zone TF1/2/3` (v0.84, default 3m/5m/15m; chọn từ dropdown S90/M1/M2/M3/M5/M15/M30) — giá phải nằm đúng phía EMA34 trên từng khung cao hơn: LONG trên, SHORT dưới (close của bar zone ĐÃ ĐÓNG, không lookahead; warmup = gate mở). Mirror theo direction.
- Alert: **edge trigger + break debounce** — 1 vertical line (dash, width default 2, màu LONG lime / SHORT đỏ — đổi được) + 1 Alert Sound (global `Alert Sound`) khi môi trường chuyển invalid → valid. Sau khi fire, môi trường phải invalid LIÊN TIẾP `Break Bars` bar mới coi là phá vỡ — wobble 1-2 bar không sinh line mới. Đổi hướng (LONG→SHORT) fire ngay.
- Settings: `AlertA1Enabled` (true), `AlertA1HistoryDays` (3), `AlertA1PeriodSeconds` (30), cond toggles (8>34, 34>89, 89>144, 144>200: true; angle: **false**), `AlertA1AngleMin` (30), `AlertA1BreakBars` (3), `AlertA1AtrPeriod` (14), `AlertA1LineWidth` (2), `AlertA1LongColor` (LimeGreen), `AlertA1ShortColor` (Red).
- Tag drawing: `K34S_ALERTA1_VL_<B/S>_<bar>` (vertical line neo theo thời gian bar 30s).
- Filter: KHÔNG áp Global Filter (độc lập). Backfill replay History Days trên series 30s (vẽ line, không sound), sync edge state cho realtime.

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
