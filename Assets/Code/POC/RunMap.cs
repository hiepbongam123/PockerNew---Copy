using System;
using System.Collections.Generic;

namespace LoRClone.Data
{
    /// <summary>Loại node trên bản đồ hành trình (Con Đường Anh Hùng).</summary>
    public enum MapNodeType { Battle, Elite, Reward, Boss, Shop, Event, Item, MiniBoss, Power }

    /// <summary>1 node trên bản đồ. id = index trong RunMap.nodes.</summary>
    [Serializable]
    public class MapNode
    {
        public int id;
        public int col;                 // cột (độ sâu) — trái sang phải
        public int row;                 // hàng trong cột (để layout)
        public int rowsInCol = 1;       // tổng số node trong cột (layout)
        public MapNodeType type;
        public int levelIndex = -1;     // LevelData dùng cho Battle/Elite/Boss; -1 = Reward
        public bool done;
        public List<int> next = new List<int>();   // id các node ở cột kế nối tới
    }

    /// <summary>
    /// BẢN ĐỒ HÀNH TRÌNH kiểu roguelike (Slay-the-Spire / Path of Champions).
    /// Nhiều cột; mỗi cột vài node có nhánh rẽ; đi 1 node mỗi cột đến khi tới Boss.
    ///
    /// Luồng chọn:
    ///   • progressCol = cột đang được chọn.
    ///   • Available() = các node ở progressCol nối từ node ĐÃ XONG (cột 0 thì tất cả).
    ///   • CompleteNode(id) → đánh dấu done + tiến sang cột kế.
    ///
    /// [Serializable] + JsonUtility-friendly → sẵn sàng cho lưu run (Mốc 6).
    /// </summary>
    [Serializable]
    public class RunMap
    {
        public int seed;
        public int columns;
        public int progressCol;                       // cột đang được chọn
        public List<MapNode> nodes = new List<MapNode>();

        public MapNode Get(int id) => (id >= 0 && id < nodes.Count) ? nodes[id] : null;

        public bool IsFinished()
        {
            // Xong khi node Boss đã done
            foreach (var n in nodes)
                if (n.type == MapNodeType.Boss && n.done) return true;
            return false;
        }

        /// <summary>Node đang có thể chọn ngay: ở progressCol và nối từ 1 node đã done (cột 0 = tất cả).</summary>
        public List<MapNode> Available()
        {
            var res = new List<MapNode>();
            foreach (var n in nodes)
            {
                if (n.col != progressCol || n.done) continue;
                if (progressCol == 0 || HasDonePredecessor(n)) res.Add(n);
            }
            return res;
        }

        public bool IsAvailable(int id)
        {
            var n = Get(id);
            if (n == null || n.done || n.col != progressCol) return false;
            return progressCol == 0 || HasDonePredecessor(n);
        }

        bool HasDonePredecessor(MapNode node)
        {
            foreach (var m in nodes)
                if (m.done && m.next.Contains(node.id)) return true;
            return false;
        }

        /// <summary>Đánh dấu node đã hoàn thành và tiến sang cột kế.</summary>
        public void CompleteNode(int id)
        {
            var n = Get(id);
            if (n == null) return;
            n.done = true;
            if (n.col + 1 > progressCol) progressCol = n.col + 1;
        }

        // ══════════════════════════════════════════════════════════
        //  GENERATION — bản đồ nhánh theo seed
        // ══════════════════════════════════════════════════════════
        /// <summary>
        /// Sinh bản đồ: cột 0 = 1 node bắt đầu, cột cuối = Boss, giữa 2–3 node/cột.
        /// levelCount = số LevelData có sẵn → map Battle/Elite/Boss vào level theo độ sâu.
        /// </summary>
        public static RunMap Generate(int seed, int columns, int levelCount, bool miniBoss = false, bool startPower = false)
        {
            columns = Math.Max(3, columns);
            levelCount = Math.Max(1, levelCount);
            var rng = new Random(seed);
            var map = new RunMap { seed = seed, columns = columns, progressCol = 0 };

            // MID-BOSS (boss 1) = CỬA BẮT BUỘC ở giữa (1 node) → hồi đầy Nexus. Boss cuối kết thúc run.
            int mid = (miniBoss && columns >= 5) ? columns / 2 : -1;

            // Số node mỗi cột: start / boss / mid = 1; cột ĐÁNH NHAU = 2–3 node (có NHÁNH để chọn đường).
            var colNodes = new List<List<MapNode>>();
            for (int c = 0; c < columns; c++)
            {
                int count = (c == 0 || c == columns - 1 || c == mid) ? 1 : 2 + rng.Next(2);
                var list = new List<MapNode>();
                for (int r = 0; r < count; r++)
                {
                    var node = new MapNode
                    { id = map.nodes.Count, col = c, row = r, rowsInCol = count, type = MapNodeType.Battle };
                    map.nodes.Add(node); list.Add(node);
                }
                colNodes.Add(list);
            }

            // Loại MẶC ĐỊNH: start Battle · boss Boss · mid MiniBoss · còn lại ĐÁNH NHAU (chủ yếu).
            for (int c = 0; c < columns; c++)
                foreach (var n in colNodes[c])
                {
                    if (c == columns - 1) { n.type = MapNodeType.Boss; n.levelIndex = levelCount - 1; }
                    else if (c == 0) { n.type = MapNodeType.Battle; n.levelIndex = 0; }
                    else if (c == mid) { n.type = MapNodeType.MiniBoss; n.levelIndex = Math.Min(c, levelCount - 1); }
                    else { n.type = MapNodeType.Battle; n.levelIndex = Math.Min(c, levelCount - 1); }
                }

            // Elite: ~35% cột đánh nhau có 1 node hoá Elite (nhánh khó hơn, thưởng cao hơn).
            for (int c = 1; c < columns - 1; c++)
            {
                if (c == mid) continue;
                if (rng.Next(100) < 35)
                {
                    var pk = colNodes[c][rng.Next(colNodes[c].Count)];
                    if (pk.type == MapNodeType.Battle) pk.type = MapNodeType.Elite;
                }
            }

            // ── PREP = ĐƯỜNG PHỤ (Vật Phẩm / Thưởng / Cửa Hàng): mỗi loại 1 node, ở 3 cột KHÁC nhau,
            //    cách nhau ≥2 cột (KHÔNG liền kề) + cách mid ≥2 → phải TÍNH khi rẽ. Chỉ chiếm 1 NHÁNH
            //    trong cột (cột vẫn còn node đánh nhau) → combat vẫn là CHÍNH, prep là lựa chọn phụ.
            var prepTypes = new[] { MapNodeType.Item, MapNodeType.Reward, MapNodeType.Shop };
            var cand = new List<int>();
            for (int c = 1; c < columns - 1; c++)
                if (c != mid && colNodes[c].Count >= 2) cand.Add(c);   // cột có ≥2 nhánh (còn chỗ battle + prep)
            for (int i = cand.Count - 1; i > 0; i--)                   // xáo theo seed
            { int j = rng.Next(i + 1); int t = cand[i]; cand[i] = cand[j]; cand[j] = t; }
            var prepCols = new List<int>();
            foreach (int c in cand)
            {
                bool ok = Math.Abs(c - mid) >= 2;
                foreach (int p in prepCols) if (Math.Abs(c - p) < 2) { ok = false; break; }
                if (ok) prepCols.Add(c);
                if (prepCols.Count >= prepTypes.Length) break;
            }
            prepCols.Sort();
            for (int i = 0; i < prepCols.Count; i++)
            {
                var col = colNodes[prepCols[i]];
                var pick = col[rng.Next(col.Count)];      // 1 nhánh thành prep; nhánh khác vẫn đánh nhau
                pick.type = prepTypes[i];
                pick.levelIndex = -1;
            }

            // Nối cạnh: mỗi node cột c → 1–2 node cột c+1; đảm bảo MỌI node cột c+1 có ≥1 predecessor.
            for (int c = 0; c < columns - 1; c++)
            {
                var cur = colNodes[c];
                var nxt = colNodes[c + 1];

                foreach (var n in cur)
                {
                    int links = 1 + (rng.Next(100) < 45 ? 1 : 0);
                    links = Math.Min(links, nxt.Count);
                    var chosen = new HashSet<int>();
                    // ưu tiên node kế cận theo hàng để nhánh không chéo quá
                    int baseIdx = nxt.Count == 1 ? 0
                        : (int)Math.Round((double)n.row / Math.Max(1, cur.Count - 1) * (nxt.Count - 1));
                    for (int k = 0; k < links; k++)
                    {
                        int pick = Math.Min(nxt.Count - 1, Math.Max(0, baseIdx + (k == 0 ? 0 : (rng.Next(3) - 1))));
                        // tránh trùng
                        int guard = 0;
                        while (chosen.Contains(pick) && guard++ < nxt.Count)
                            pick = (pick + 1) % nxt.Count;
                        if (chosen.Add(pick)) n.next.Add(nxt[pick].id);
                    }
                }

                // Bảo đảm reachability: node cột c+1 nào chưa có predecessor → nối từ node gần nhất cột c
                foreach (var target in nxt)
                {
                    bool hasPred = false;
                    foreach (var n in cur) if (n.next.Contains(target.id)) { hasPred = true; break; }
                    if (!hasPred)
                    {
                        int baseIdx = cur.Count == 1 ? 0
                            : (int)Math.Round((double)target.row / Math.Max(1, nxt.Count - 1) * (cur.Count - 1));
                        cur[Math.Min(cur.Count - 1, Math.Max(0, baseIdx))].next.Add(target.id);
                    }
                }
            }

            // ẢI POWER MỞ ĐẦU: chèn 1 node Power ở cột 0 (trước combat) → chọn Lõi Sức Mạnh vào hành trình.
            // Dời mọi cột hiện có +1 (cạnh dùng id nên KHÔNG vỡ), Power nối tới toàn bộ node combat đầu (giờ ở cột 1).
            if (startPower)
            {
                foreach (var n in map.nodes) n.col += 1;
                var power = new MapNode { id = map.nodes.Count, col = 0, row = 0, rowsInCol = 1, type = MapNodeType.Power, levelIndex = -1 };
                foreach (var n in map.nodes) if (n.col == 1) power.next.Add(n.id);
                map.nodes.Add(power);
                map.columns = columns + 1;
                map.progressCol = 0;   // phải qua Power trước
            }

            return map;
        }
    }
}