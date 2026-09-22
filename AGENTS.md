# AGENTS.md

Hướng dẫn cho AI coding agent làm việc trong repo này. Đọc file này trước khi sửa code.

## Tổng quan dự án

Game thẻ bài kiểu **Legends of Runeterra (LoR)** clone, viết bằng **Unity 6 (6000.0.36f1) + C#**, đang được reskin phong cách sang **Honkai: Star Rail — Amphoreus** (xem `Assets/Data/CardCharactor/Mydei region`).

Có 3 chế độ chơi:
- **PvE / Path of Champions (POC)** — chạy roguelike campaign, relic, power, chest, run map.
- **PvP** — đối kháng online qua Photon (PUN2), account/rank/leaderboard qua Unity Gaming Services.
- **POCxPVP** — mang loadout champion từ POC vào đấu PvP (gauntlet).

> Lưu ý: tên repo/thư mục là "PockerNew" nhưng **đây KHÔNG phải game poker** — chỉ là tên cũ.

## Tech stack

- Unity **6000.0.36f1**, Universal Render Pipeline (URP 17.0.3)
- C# / .NET (assembly mặc định `Assembly-CSharp`, không tách asmdef cho code game)
- **Photon PUN2** (`Assets/Photon`) — networking PvP
- **Unity Gaming Services**: Authentication, Cloud Save, Leaderboards
- Input System 1.12, TextMesh Pro, Timeline (cinematic level-up)
- UI dựng bằng **uGUI** (không phải UI Toolkit)

## Cấu trúc code (`Assets/Code`)

Kiến trúc tách lớp **Data / Model / View / Controller / Skills / Net**, namespace gốc `LoRClone.*`:

| Thư mục | Namespace | Vai trò |
|---|---|---|
| `Assets/Code/*` (root) | `LoRClone.Data`, `LoRClone.Model` | ScriptableObject data (`CardData`, `DeckData`, `SkillData`...) + model runtime (`CardModel`, `PlayerModel`, `GameModel`) |
| `GameController/` | `LoRClone.Controller` | Luật game — `GameController` là **partial class** chia theo feature slice |
| `Code Skill/` | `LoRClone.Skills` | Mỗi skill = 1 class `ScriptableObject` kế thừa `SkillData` |
| `View/` | `LoRClone.View` | uGUI view, zone, card view (nhiều partial: `CardView.*`) |
| `POC/` | — | Path of Champions: campaign, relic, power, run map, chest |
| `PVP/` | — | Deck builder, card library, rank/leaderboard, account (UGS) |
| `POCxPVP/` | — | Cầu nối loadout POC → PvP |
| `Photon/` | `LoRClone.Net` | DTO, codec, bridge, launcher cho multiplayer |
| `Lobby/` | — | Menu chính, settings |
| `Editor/` | `LoRClone.Tools/Tests` | Editor tool + PlayMode test |

## Các pattern quan trọng (tuân theo khi sửa/thêm code)

### 1. GameController = partial class theo feature
`GameController` được chia thành nhiều file `GameController.<Feature>.cs` (Combat, Spells, Triggers, LevelUp, UnitPlay, Equipment, Keywords, Predict, Concede, AutoPlay...). **Thêm logic mới → tạo/dùng đúng slice feature**, đừng nhồi hết vào `GameController.cs`.

### 2. Skill = 1 class ScriptableObject
Tạo skill mới = tạo class kế thừa `SkillData` trong `Code Skill/`, override `Execute(CardModel source, GameContext ctx, int p1, int p2, int p3)`. Quy tắc:
- **KHÔNG đặt logic game vào `SkillData`** — chỉ data + `Execute()`. Gọi ngược về `GameController` qua `ctx.controller` khi cần thao tác cấp game (summon, obliterate, damage nexus...).
- Params `p1/p2/p3` = override từ `CardAbility`; `-1` = dùng default của asset.
- Skill cần chọn mục tiêu: override `RequiredTargetType`; hoặc dùng `AutoTargetMode` + `SkillTargetResolver.Resolve(...)` để tự chọn target không cần UI.
- Thêm `[CreateAssetMenu(menuName = "LoRClone/Skills/...")]` để tạo asset trong Editor.
- File mẫu chuẩn để tham khảo: `Code Skill/SkillDamageTarget.cs`.

### 3. GameContext thay cho Singleton
Skill nhận state qua `GameContext` (snapshot: `game`, `owner`, `opponent`, `controller`, `targetCards`) truyền vào `Execute()`, **không** truy cập trực tiếp singleton nếu tránh được. `GameController.Instance` chỉ là fallback.

### 4. Data-driven bằng ScriptableObject
Card, deck, skill, champion, relic, power... đều là ScriptableObject asset trong `Assets/Data/*` và `Assets/Resources/Decks`. Thêm nội dung game (thẻ mới, skill mới) thường là **tạo asset + gắn skill**, không sửa code luật.

### 5. Model tách khỏi View
`GameModel`/`CardModel`/`PlayerModel` là POCO runtime (không kế thừa MonoBehaviour). View đăng ký sự kiện state-changed để render. Giữ luật game trong Model/Controller, chỉ hiển thị trong View.

## Quy ước code

- **Comment & tooltip viết bằng tiếng Việt** — giữ nguyên phong cách này, viết code mới cũng comment tiếng Việt.
- Namespace theo lớp: `LoRClone.Data` / `.Model` / `.Controller` / `.View` / `.Skills` / `.Net`.
- Ưu tiên tách file partial theo feature khi class phình to (đã áp dụng cho `GameController` và `CardView`).
- Enum trạng thái/pha đã định nghĩa sẵn (`GamePhase`, `CardLocation`, `CardState`, `SkillTrigger`, `CardType`, `SpellSpeed`) — tái dùng, đừng dùng magic string. Tag thẻ dùng hằng trong `Tags`.

## Scene & entry point

- `Assets/Scenes/LobbyScreen.unity` — menu/lobby vào game.
- `Assets/Scenes/GamePlayScene.unity` — màn chơi chính (`GameController` + `GameView`).

## Build / chạy

- Mở bằng **Unity 6000.0.36f1** (khớp `ProjectSettings/ProjectVersion.txt`, sai version dễ lỗi).
- Package resolve tự động từ `Packages/manifest.json`.
- PvP cần cấu hình Photon AppId + đăng nhập UGS (Authentication).
- Test: PlayMode test trong `Assets/Code/Editor` (`PoCTests_1.cs`) chạy qua Unity Test Runner.

## Đừng đụng vào

- `Assets/Photon/**` — SDK bên thứ ba (PUN2), không sửa trừ khi thật sự cần.
- `Library/`, `Temp/`, `obj/`, `Logs/`, `*.csproj`, `*.sln` — auto-generate, đã ignore (xem `.gitignore`). Không commit.
- File `.meta` của Unity — **luôn giữ và commit kèm** asset tương ứng, đừng xóa lẻ.

## Git

- Repo: `https://github.com/hiepbongam123/PockerNew---Copy.git`, branch chính `main`.
