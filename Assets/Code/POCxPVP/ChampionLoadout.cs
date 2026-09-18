using System.Collections.Generic;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// BƯỚC 1 (nền tảng PvP-RPG) — "VAN" gói toàn bộ nâng cấp của 1 champion thành DỮ LIỆU THUẦN.
    ///
    /// VÌ SAO: PvP là lockstep — 2 máy phải mô phỏng giống hệt. Hiện GameController TẮT sạch lớp PoC
    /// khi networkMode vì buff PoC đọc save LOCAL (mỗi máy khác nhau → desync). Muốn mang champion đã
    /// nâng vào PvP: KHÔNG áp buff local, mà đóng gói loadout này (tên + số) → gửi qua mạng → áp GIỐNG
    /// HỆT 2 máy. File này là "van" đó: TẦNG TRÊN (đọc save, bất đối xứng) tách khỏi TẦNG DƯỚI (áp trận).
    ///
    /// AN TOÀN: file này CHỈ ĐỌC + tạo dữ liệu. KHÔNG sửa CardModel/PlayerModel/trận nào. Thêm vào
    /// project KHÔNG đổi hành vi PvE/PvP hiện tại. Nó chỉ có tác dụng khi BƯỚC 2 (ApplyLoadout) dùng tới.
    ///
    /// NGUYÊN TẮC: chỉ chứa KẾT QUẢ ĐÃ CHỐT (tên món, tên lá, số) — KHÔNG chứa nguồn ngẫu nhiên.
    /// </summary>
    [System.Serializable]
    public class ChampionLoadout
    {
        public string championName = "";
        public int masteryLevel = 1;
        public int star = 1;
        public int constellationStars = 0;              // số node Chòm Sao đã mua (tuần tự 0..6)
        public string[] equippedItemNames = new string[0]; // trang bị champion ĐANG ĐEO (đã sort — deterministic)
        public string[] equippedItemDescs = new string[0]; // mô tả từng món (KHỚP index equippedItemNames) — HIỂN THỊ không cần library
        public int[] equippedItemRarities = new int[0];    // (int)ItemRarity, KHỚP index — để tô màu ô
        public string[] deckCardNames = new string[0];     // deck run (đã áp nâng lá) — GIỮ THỨ TỰ (ảnh hưởng shuffle theo seed)

        // ── META "CORE AN TOÀN" (áp ngay StartGame, không đụng timing) — số ĐÃ CHỐT ở máy local ──
        // startNexusBonus  = máu Nexus theo Mastery + bonusNexus của signaturePower/starPerks/Chòm Sao.
        // allUnitsCostReduction = tổng costReduction của các power đó (giảm giá MỌI đơn vị — luật PoC phe người chơi).
        // (atk/hp toàn quân + keyword của power BỊ CHẶN cho phe người chơi — buff đó chỉ đến từ TRANG BỊ.)
        public int startNexusBonus = 0;
        public int allUnitsCostReduction = 0;

        // ── META cần TIMING (áp SAU mulligan vòng 1, không phải StartGame) — số đã chốt local ──
        // startManaBonus = +mana khởi đầu (item.bonusMana + power.bonusMana). startDrawBonus = +rút bài (power.bonusDraw).
        public int startManaBonus = 0;
        public int startDrawBonus = 0;

        // ── BUFF LÁ CHAMPION từ TRANG BỊ — CHỐT SẴN THÀNH SỐ lúc build (máy có save/library của mình).
        //    Applier áp THẲNG số này, KHÔNG đọc lại CardItem.AllFromLibrary() → tất định tuyệt đối, không desync
        //    dù thư viện item chưa gán trên máy kia. (Trước đây resolve ở applier → 1 máy có library 1 máy không → lệch.)
        public int championAtkBonus = 0;
        public int championHpBonus = 0;
        public int championCostReduction = 0;
        public int[] championKeywords = new int[0];      // (int)KeywordType — enum serialize được
        public string[] championSkillNames = new string[0]; // tên SkillData (resolve lại qua library — chỉ dùng cho skill)

        // ── Dựng từ tiến trình LOCAL (PvE, hoặc phe MÌNH trước khi vào PvP) ──
        /// <summary>
        /// Đọc ProgressStore + ChampionDeckStore của MÁY NÀY đúng 1 lần → chốt thành loadout.
        /// Đây là chỗ DUY NHẤT được phép đọc save. Sau bước này chỉ còn dữ liệu thuần.
        /// </summary>
        public static ChampionLoadout BuildFromProgress(ChampionData champ)
        {
            var lo = new ChampionLoadout();
            if (champ == null) return lo;

            string name = champ.championName ?? "";
            lo.championName = name;
            lo.masteryLevel = ProgressStore.GetMasteryLevel(name);
            lo.star = ProgressStore.GetStar(name);
            lo.constellationStars = CountConstellationStars(name);
            // equippedItemNames/Descs/Rarities dựng CHUNG 1 lượt bên dưới (cùng nguồn với stat) → luôn khớp.
            lo.deckCardNames = ResolveDeckCardNames(champ, lo.masteryLevel);

            // Gộp bonus CORE (power + mastery) NGAY tại đây (đọc save LOCAL cho phép) → chốt thành số thuần.
            int nexus = (lo.masteryLevel - 1) * ProgressStore.NexusPerLevel;   // máu Nexus theo cấp Mastery
            int cost = 0, mana = 0, draw = 0;
            foreach (var p in ActivePowers(champ))
            {
                nexus += p.bonusNexus;
                cost += p.costReduction;
                mana += p.bonusMana;
                draw += p.bonusDraw;
            }
            // ── Resolve TRANG BỊ champion → CHỐT THÀNH SỐ tại đây (local có library) → không lệ thuộc library ở applier.
            //   ĐỒNG THỜI chốt luôn TÊN + MÔ TẢ + ĐỘ HIẾM từng món → truyền qua mạng → máy xem hiện chữ KHÔNG cần library.
            int iAtk = 0, iHp = 0, iCost = 0, iNexus = 0, iMana = 0;
            var kws = new List<int>();
            var skillNames = new List<string>();
            var disp = new List<(string name, string desc, int rarity)>();
            foreach (var it in LoRClone.Model.CardItem.EquippedChampionItems(name, lo.masteryLevel))
            {
                if (it == null) continue;
                iAtk += it.atkBonus; iHp += it.hpBonus; iCost += it.costReduction;
                iNexus += it.bonusNexus; iMana += it.bonusMana;
                if (it.grantKeyword.HasValue) kws.Add((int)it.grantKeyword.Value);
                if (it.skill != null && !string.IsNullOrEmpty(it.skill.name)) skillNames.Add(it.skill.name);
                if (!string.IsNullOrEmpty(it.itemName)) disp.Add((it.itemName, it.Describe(), (int)it.rarity));
            }
            lo.championAtkBonus = iAtk;
            lo.championHpBonus = iHp;
            lo.championCostReduction = iCost;
            lo.championKeywords = kws.ToArray();
            lo.championSkillNames = skillNames.ToArray();

            // Sort theo TÊN Ordinal → 2 máy cùng thứ tự (chữ ký khớp) + 3 mảng luôn khớp index.
            disp.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            lo.equippedItemNames = new string[disp.Count];
            lo.equippedItemDescs = new string[disp.Count];
            lo.equippedItemRarities = new int[disp.Count];
            for (int i = 0; i < disp.Count; i++)
            { lo.equippedItemNames[i] = disp[i].name; lo.equippedItemDescs[i] = disp[i].desc; lo.equippedItemRarities[i] = disp[i].rarity; }

            nexus += iNexus;   // máu Nexus từ item gộp vào startNexusBonus
            mana += iMana;     // +mana khởi đầu từ item

            lo.startNexusBonus = Mathf.Max(0, nexus);
            lo.allUnitsCostReduction = Mathf.Max(0, cost);
            lo.startManaBonus = Mathf.Max(0, mana);
            lo.startDrawBonus = Mathf.Max(0, draw);
            return lo;
        }

        // Các RunPower ĐANG hiệu lực của champion: signaturePower + starPerks theo Sao + node Chòm Sao đã mua.
        static IEnumerable<RunPower> ActivePowers(ChampionData champ)
        {
            if (champ == null) yield break;
            string name = champ.championName ?? "";

            if (champ.signaturePower != null && champ.signaturePower.IsMeaningful)
                yield return champ.signaturePower;

            int star = ProgressStore.GetStar(name);
            if (champ.starPerks != null)
                for (int st = 2; st <= star; st++)          // Sao 2..star → starPerks[st-2]
                {
                    int idx = st - 2;
                    if (idx >= 0 && idx < champ.starPerks.Count
                        && champ.starPerks[idx] != null && champ.starPerks[idx].IsMeaningful)
                        yield return champ.starPerks[idx];
                }

            if (champ.constellation != null)
                for (int i = 0; i < champ.constellation.Count; i++)
                    if (ProgressStore.IsConstellationBought(name, i))
                    {
                        var node = champ.constellation[i];
                        if (node != null && node.effect != null && node.effect.IsMeaningful)
                            yield return node.effect;
                    }
        }

        // Chòm Sao mở TUẦN TỰ 0→6: đếm số node đã mua liên tiếp từ đầu (khớp CampaignRun.ConstellationStars).
        static int CountConstellationStars(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            int n = 0;
            for (int i = 0; i < 6; i++)
            {
                if (ProgressStore.IsConstellationBought(name, i)) n++;
                else break;
            }
            return n;
        }

        // Trang bị champion ĐANG ĐEO → tên món, SORT ordinal → 2 máy ra cùng thứ tự → chữ ký khớp.
        static string[] ResolveEquippedItemNames(string name, int masteryLevel)
        {
            var names = new List<string>();
            foreach (var it in LoRClone.Model.CardItem.EquippedChampionItems(name, masteryLevel))
                if (it != null && !string.IsNullOrEmpty(it.itemName)) names.Add(it.itemName);
            names.Sort(System.StringComparer.Ordinal);
            return names.ToArray();
        }

        // Deck run: ưu tiên deck champion ĐÃ LƯU → không thì starterDeck. GIỮ THỨ TỰ.
        // Sau đó ÁP NÂNG BÀI (deckUpgrades): mastery >= up.level → đổi from → to (cộng dồn).
        //
        // ★ FIX ĐỒNG BỘ: đọc tên deck TỪ PlayerPrefs qua ChampionDeckStore.GetNames — nguồn BỀN, khớp PoC.
        //   KHÔNG chỉ dựa TryGetResolved (cache RAM) vì cache đó RỖNG lúc mới chạy game (chỉ đầy khi đã
        //   mở champion trong PoC) → trước đây arena rơi về starterDeck ⇒ lệch với deck đã lưu ở PoC.
        static string[] ResolveDeckCardNames(ChampionData champ, int masteryLevel)
        {
            var list = new List<string>();
            string champName = champ.championName ?? "";

            // 1) Deck đã lưu (PlayerPrefs) — bền qua restart, đúng thứ tự đã lưu.
            if (ChampionDeckStore.HasSaved(champName))
            {
                var names = ChampionDeckStore.GetNames(champName);
                if (names != null) foreach (var n in names) if (!string.IsNullOrEmpty(n)) list.Add(n);
            }
            // 2) Fallback: cache resolved trong session (nếu HasSaved rỗng nhưng cache có).
            if (list.Count == 0
                && ChampionDeckStore.TryGetResolved(champName, out var saved)
                && saved != null && saved.Count > 0)
            {
                foreach (var c in saved) if (c != null) list.Add(c.cardName);
            }
            // 3) Fallback cuối: starterDeck.
            if (list.Count == 0 && champ.starterDeck != null && champ.starterDeck.cards != null)
            {
                foreach (var c in champ.starterDeck.cards) if (c != null) list.Add(c.cardName);
            }

            // Đảm bảo lá CHAMPION có trong deck (PoC BuildRunDeck cũng chèn nếu thiếu) → chèn đầu nếu vắng.
            if (champ.championCard != null && !string.IsNullOrEmpty(champ.championCard.cardName)
                && !list.Contains(champ.championCard.cardName))
                list.Insert(0, champ.championCard.cardName);

            // NÂNG BÀI theo Mastery — đổi tên lá yếu → lá mạnh. Deterministic (dựa masteryLevel đã chốt).
            if (champ.deckUpgrades != null)
                foreach (var up in champ.deckUpgrades)
                    if (up != null && up.from != null && up.to != null && masteryLevel >= up.level)
                        for (int i = 0; i < list.Count; i++)
                            if (list[i] == up.from.cardName) list[i] = up.to.cardName;

            return list.ToArray();
        }

        // ── CHỮ KÝ DETERMINISTIC — 2 máy PvP phải khớp (bước 4 feed vào desync-check của handshake) ──
        /// <summary>
        /// Gộp toàn bộ loadout thành 1 chuỗi ổn định. 2 máy build ra khác chuỗi này → sẽ desync khi
        /// áp buff → bắt lỗi NGAY ở bắt tay thay vì giữa trận.
        /// </summary>
        public string Signature()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(championName).Append('|');
            sb.Append('m').Append(masteryLevel).Append('|');
            sb.Append('s').Append(star).Append('|');
            sb.Append('c').Append(constellationStars).Append('|');
            sb.Append("nx").Append(startNexusBonus).Append('|');
            sb.Append("cr").Append(allUnitsCostReduction).Append('|');
            sb.Append("mn").Append(startManaBonus).Append('|');
            sb.Append("dr").Append(startDrawBonus).Append('|');
            sb.Append("ca").Append(championAtkBonus).Append('|');
            sb.Append("ch").Append(championHpBonus).Append('|');
            sb.Append("cc").Append(championCostReduction).Append('|');
            sb.Append("ck[");
            if (championKeywords != null) foreach (var k in championKeywords) sb.Append(k).Append(';');
            sb.Append("]|cs[");
            if (championSkillNames != null) foreach (var n in championSkillNames) sb.Append(n).Append(';');
            sb.Append("]|eq[");
            if (equippedItemNames != null) foreach (var n in equippedItemNames) sb.Append(n).Append(';');
            sb.Append("]|dk[");
            if (deckCardNames != null) foreach (var n in deckCardNames) sb.Append(n).Append(';');
            sb.Append(']');
            return sb.ToString();
        }

        // ── JSON (bước 4 gửi qua PUN RPC dạng string, giống PvpDeckSelection) ──
        public string ToJson() => JsonUtility.ToJson(this);
        public static ChampionLoadout FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return new ChampionLoadout();
            return JsonUtility.FromJson<ChampionLoadout>(json) ?? new ChampionLoadout();
        }

        // ── Debug: in ra để tự kiểm bằng mắt trong Console (KHÔNG bắt buộc) ──
        public string Describe()
        {
            string items = (equippedItemNames != null && equippedItemNames.Length > 0)
                ? string.Join(", ", equippedItemNames) : "(không)";
            return $"[Loadout] {championName} — Mastery {masteryLevel}, {star} Sao, Chòm Sao {constellationStars} · " +
                   $"Trang bị: {items} · Deck: {(deckCardNames != null ? deckCardNames.Length : 0)} lá";
        }
    }
}