# MVP Roadmap — lorxhsr (LoR × HSR Amphoreus)

> Tài liệu định hướng: MVP là gì, hiện trạng ra sao, làm gì trước / nâng cấp gì.
> Cập nhật: 2026-09-23. Nguồn: audit code trực tiếp (~39k dòng, nhánh `hsrxlor`).

## 0. Quyết định định hướng (chốt cùng bạn)

- **Ưu tiên số 1: cốt lõi gameplay phải VUI trước.** Vận hành tốt mà chơi chán thì không có động lực phát triển. → MVP không phải "thêm mode", mà là **1 lát cắt dọc (vertical slice) chơi cực sướng**.
- Nền tảng: **PC + Mobile** → thiết kế cảm ứng + responsive tính từ MVP, không để sau.
- Kiếm tiền: **Free + IAP / gacha** → **HOÃN**. Không xây kinh tế/gacha lên trên một gameplay chưa chứng minh là vui. Kinh tế thuộc giai đoạn sau (P2).

**Định nghĩa MVP ở đây:** một **run PVE ngắn (~15–20 phút)** — vào trận → đánh vài trận card battle → nhận thưởng → chơi lại — nhưng mọi khâu *cảm giác đã* (game feel), *rõ ràng* (người mới hiểu ngay), và *ổn định* (không crash/softlock). Chọn PVE làm "vỏ" vì nó khả thi nhất cho solo dev và bạn đã có sẵn hệ POC (Path of Champions).

## 1. Hiện trạng (từ audit)

**Đã vững — giữ nguyên, chỉ tinh chỉnh**
- Kiến trúc tách lớp tốt (Data / Model / View / Controller / Skills / Net), model là POCO → dễ test.
- Luật core LoR khá đầy đủ: mana, spell speed (Burst/Fast/Slow), attack token, mulligan, spell stack, level-up, keyword, equipment, predict.
- Hệ skill data-driven (46 asset skill, base `SkillData` + `SkillTargetResolver`) — mở rộng nội dung không cần sửa luật.
- Luồng thắng/thua vững: `GamePhase.GameOver` được guard khắp nơi (93 tham chiếu).
- Save có chốt an toàn: JSON ở `persistentDataPath` + PlayerPrefs, cấm ghi đè khi load lỗi.
- Đã có nền game-feel: hit-stop, các delay combat/clash, cinematic level-up, CombatAnimator.
- Art HSR Amphoreus có thật (Okhema, Kremnos, Janusopolis, Grove of Epiphany), 333 ảnh, 70 file audio.

**Ổn nhưng cần nâng**
- **AI địch**: ~741 dòng, heuristic tham lam (đánh unit rẻ nhất, tấn công tất cả, block theo trade). Chạy được nhưng dễ đoán, chưa có độ khó theo tầng.
- **Cảm ứng**: 0 xử lý touch riêng; dùng chuột + pointer uGUI (13/13). Tap cơ bản chạy, nhưng **hover tooltip, right-click inspect, drag-targeting** chưa hợp mobile (13 tham chiếu chuột cần thay).
- **Responsive**: có CanvasScaler/safe-area (26 tham chiếu) nhưng cần kiểm thật trên tỉ lệ dọc điện thoại.
- **Test**: 26 test nhưng dồn ở POC; combat/spell/keyword của `GameController` gần như chưa có test.

**Thiếu / rủi ro**
- **Không có tutorial/onboarding** — người mới phải tự mò luật. Chí mạng với game F2P/mobile.
- **Nội dung khiêm tốn**: 19 player unit, 13 enemy unit, 11 spell, 4 champion, 10 deck. Đủ demo, chưa đủ chiều sâu để giữ chân.
- **PVP mạng chưa trọn**: `ActionCodec` v2 (targeting tương tác qua mạng) **chưa làm** → spell/skill cần chọn mục tiêu KHÔNG chạy được online.
- **UI dựng 100% bằng code** (chỉ 1 prefab) → khó chỉnh art, khó responsive, khó bàn giao.
- `AutoPlay` Phase 2 chưa làm; reset-progress còn TODO chưa nối logic.

## 2. Tiêu chí "MVP đạt" (nghiệm thu)

Coi là đạt khi TẤT CẢ đúng:
1. Một run 15–20 phút chạy đầu → cuối, **không crash, không softlock**.
2. Người chưa từng chơi **hiểu 4 thao tác cơ bản trong ~2 phút** mà không cần bạn giải thích (rút bài, đánh unit, tấn công/chặn, cast spell).
3. Chạy mượt trên **1 PC tầm trung + 1 điện thoại tầm trung** (mục tiêu 60fps, không tụt khung khi combat).
4. **≥3 người test** chơi xong tự muốn "chơi ván nữa" (đây là thước đo VUI thật sự).
5. Toàn bộ test EditMode/PlayMode PASS (theo quy tắc làm việc đã chốt).

## 3. Thứ tự ưu tiên

### P0 — Làm BATTLE LOOP vui & rõ ràng (ưu tiên cao nhất)
Đây là phần quyết định "vui hay chán". Chưa đụng nội dung mới hay mode mới.
- **Game feel / juice**: số sát thương bật lên, screen shake nhẹ khi clash/nexus, VFX+SFX chơi bài nhất quán, "moment" khi thắng/thua (slow-mo + âm thanh + banner).
- **Rõ ràng (clarity)**: tap-hold hiện tooltip keyword; hiển thị nổi bật ai đang giữ attack token; "telegraph" hành động AI (đang nghĩ / sắp tấn công); log hành động ngắn gọn.
- **Chống softlock**: rà kỹ targeting + spell stack, đảm bảo mọi nhánh đều thoát được (đặc biệt khi target chết giữa chừng).
- **Onboarding tối thiểu**: 1 trận **tutorial kịch bản** dạy 4 thao tác — KHÔNG cần hệ thống tutorial tổng quát, chỉ 1 màn scripted.
- **Balance pass sơ bộ**: đường cong mana, sát thương, và `loopMode` — chơi thử để không có combo gãy hoặc trận lê thê.

### P1 — Đủ dày cho vertical slice + đa nền tảng
- **Cảm ứng thật**: thay hover/right-click bằng tap-hold; drag-targeting mượt trên touch; UI portrait + safe-area kiểm trên máy thật.
- **Nội dung tối thiểu VUI**: nâng lên ~30–40 thẻ có tương tác rõ, định hình **2–3 archetype** (vd aggro / midrange / combo) để người chơi cảm nhận lối chơi khác nhau; 3–4 tướng POC khác biệt thật sự.
- **AI khá hơn**: thêm đánh giá lượt (không all-in mù), block thông minh, cast spell đúng thời điểm; **độ khó tăng theo tầng**.
- **Meta-loop roguelike tối thiểu**: 1 run → thưởng → mở thẻ/power → chơi lại. Chưa cần gacha/tiền tệ.

### P2 — CHỈ sau khi core đã được xác nhận là vui
- Kinh tế: gacha, IAP, shop, tiền tệ, cân bằng drop.
- PVP: hoàn thiện `ActionCodec` v2 (targeting tương tác qua mạng) rồi mới bật spell-target online; matchmaking, chống desync.
- Live-ops, account cloud mở rộng, leaderboard, mùa (season), nhiều nội dung.

## 4. Nợ kỹ thuật cần trả (làm song song, từng bước có test)
- Mở rộng **test** cho `GameController` (combat, spell stack, keyword, level-up) — hiện là điểm mù lớn nhất.
- **UI**: cân nhắc chuyển các màn chính sang prefab + CanvasScaler chuẩn (hoặc ít nhất tách hằng số layout) để responsive + dễ chỉnh art.
- **Content pipeline**: tạo thẻ đang thủ công trong Inspector — khi số thẻ tăng, làm tool/CSV import để nhập nhanh, ít lỗi.
- Chốt PVP thuộc P2 vì `ActionCodec` v2 chưa làm; đừng để nửa vời gây kỳ vọng sai.
- Nối logic thật cho reset-progress (`LobbySettingsMenuView`), dọn `AutoPlay` Phase 2.

## 5. Cách đo "đã đủ vui chưa"
- Playtest có cấu trúc: đo retention ván-2, thời lượng phiên, vị trí người chơi bỏ cuộc, và câu hỏi thẳng "muốn chơi lại không?".
- Ghi log mỗi run (thắng/thua, số turn, thẻ dùng nhiều) → tự phân tích cân bằng thay vì đoán.

## 6. Bắt tay ngay — 1–2 tuần đầu (mỗi việc = 1 commit + test, theo quy tắc đã chốt)
1. **Juice combat**: damage number pop + screen shake + SFX clash → cảm giác "nặng" mỗi đòn.
2. **Tap-hold tooltip keyword**: người mới đọc được keyword mà không rời trận (nền cho cả mobile).
3. **Rà softlock targeting/spell-stack** + thêm test PlayMode cho các nhánh target chết giữa chừng.
4. **Màn tutorial kịch bản** 1 trận dạy 4 thao tác.
5. **Bảng "moment" thắng/thua** (slow-mo + banner + âm thanh).

→ Nói mình chọn việc số mấy để bắt đầu; mình sẽ làm từng việc kèm test và commit riêng.
