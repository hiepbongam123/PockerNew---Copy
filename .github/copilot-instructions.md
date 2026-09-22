# Copilot / AI agent instructions

Toàn bộ hướng dẫn cho AI agent (kiến trúc, pattern, quy ước, build) nằm trong **[`AGENTS.md`](../AGENTS.md)** ở thư mục gốc repo. Đọc file đó trước khi sửa code.

Tóm tắt nhanh:
- Game thẻ bài kiểu **Legends of Runeterra**, Unity 6 + C# (namespace `LoRClone.*`), đang reskin sang HSR Amphoreus. Tên "PockerNew" gây hiểu nhầm — **không phải game poker**.
- `GameController` là **partial class** chia theo feature slice; thêm logic vào đúng slice.
- Skill mới = class kế thừa `SkillData` trong `Assets/Code/Code Skill/`; chỉ data + `Execute()`, gọi ngược về `GameController` qua `ctx.controller`.
- Data-driven bằng ScriptableObject trong `Assets/Data/*`; tách lớp Data/Model/View/Controller.
- Comment viết bằng **tiếng Việt**. Không sửa `Assets/Photon/**`, không commit file auto-gen (`Library/`, `*.csproj`, `*.sln`), luôn giữ file `.meta`.
