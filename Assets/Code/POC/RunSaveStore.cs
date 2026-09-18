using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// MỐC 6 — Lưu/khôi phục HÀNH TRÌNH (run) ra JSON để chơi tiếp sau khi tắt game.
    /// File: persistentDataPath/campaign_run.json.
    ///
    /// Reference (ScriptableObject) KHÔNG lưu trực tiếp được → lưu theo TÊN, resolve khi load:
    ///   champion ← CampaignData.champions;  relic ← CampaignData.relicPool;
    ///   card     ← CardLibrary.allCards (fallback: quét champion deck / starter / reward).
    /// RunMap + RunPower là [Serializable] dữ liệu thuần → JsonUtility lưu trực tiếp.
    /// </summary>
    public static class RunSaveStore
    {
        [Serializable]
        class RunDTO
        {
            public bool active;
            public string championName = "";
            public string adventureName = "";   // M1
            public List<RunPower> powers = new List<RunPower>();
            public List<RunPower> enemyPowers = new List<RunPower>();
            public List<string> relicNames = new List<string>();
            public List<string> powerNames = new List<string>();   // Power (Lõi vận hành) theo tên
            public List<string> deckCardNames = new List<string>();
            public RunMap map;
            public int currentNodeId = -1;
            public bool pendingRelic;
            public int levelIndex = -1;
            public int gold = 0;          // M10
            public int difficulty = 1;    // M10
            public int runStartXp = 0;    // tổng kết
            public int runStartEssence = 0;
            public int runStartCores = 0; // M11: farm Lõi trong run
            public int nexusMax = 0;      // M13: HP Nexus xuyên suốt
            public int nexusCurrent = 0;
            public bool nexusInit = false;
            public List<LoRClone.Model.RunItems.SaveEntry> items = new List<LoRClone.Model.RunItems.SaveEntry>(); // item nhặt
        }

        static string FilePath => Path.Combine(Application.persistentDataPath, "campaign_run.json");

        public static bool HasSave()
        {
            try { return File.Exists(FilePath); } catch { return false; }
        }

        /// <summary>Ghi trạng thái run hiện tại (CampaignRun + CampaignContext) ra JSON.</summary>
        public static void Save()
        {
            if (CampaignContext.map == null) { Clear(); return; }

            var dto = new RunDTO
            {
                active = true,
                championName = CampaignRun.champion != null ? CampaignRun.champion.championName : "",
                adventureName = CampaignRun.adventure != null ? CampaignRun.adventure.adventureName : "",
                map = CampaignContext.map,
                currentNodeId = CampaignContext.currentNodeId,
                pendingRelic = CampaignContext.pendingRelic,
                levelIndex = CampaignContext.levelIndex,
                gold = CampaignRun.gold,
                difficulty = CampaignRun.difficulty,
                runStartXp = CampaignRun.runStartXp,
                runStartEssence = CampaignRun.runStartEssence,
                runStartCores = CampaignRun.runStartCores,
                nexusMax = CampaignRun.nexusMax,
                nexusCurrent = CampaignRun.nexusCurrent,
                nexusInit = CampaignRun.nexusInit,
            };
            foreach (var p in CampaignRun.Collected) dto.powers.Add(p);
            foreach (var p in CampaignRun.EnemyPowers) dto.enemyPowers.Add(p);
            foreach (var r in CampaignRun.Relics) if (r != null) dto.relicNames.Add(r.relicName);
            foreach (var p in CampaignRun.Powers) if (p != null) dto.powerNames.Add(p.powerName);
            foreach (var c in CampaignRun.RunDeck) if (c != null) dto.deckCardNames.Add(c.cardName);
            dto.items = LoRClone.Model.RunItems.Snapshot();   // lưu item nhặt (RunItems)

            try { File.WriteAllText(FilePath, JsonUtility.ToJson(dto, true)); }
            catch (Exception e) { Debug.LogError($"[RunSaveStore] Lỗi ghi run: {e.Message}"); }
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception e) { Debug.LogWarning($"[RunSaveStore] {e.Message}"); }
        }

        /// <summary>Đọc save + khôi phục vào CampaignRun/CampaignContext. Trả về true nếu resume được.</summary>
        public static bool TryRestore(CampaignData campaign, CardLibrary library)
        {
            if (campaign == null || !HasSave()) return false;

            RunDTO dto;
            try { dto = JsonUtility.FromJson<RunDTO>(File.ReadAllText(FilePath)); }
            catch (Exception e) { Debug.LogWarning($"[RunSaveStore] Lỗi đọc run: {e.Message}"); return false; }

            if (dto == null || !dto.active || dto.map == null || dto.map.nodes == null || dto.map.nodes.Count == 0)
                return false;

            CampaignRun.ResetRun();

            // champion theo tên
            if (!string.IsNullOrEmpty(dto.championName) && campaign.champions != null)
                foreach (var ch in campaign.champions)
                    if (ch != null && ch.championName == dto.championName) { CampaignRun.champion = ch; break; }
            // M7: khôi phục cấp sao (suy lại từ mastery hiện tại)
            if (CampaignRun.champion != null)
                CampaignRun.SetChampionStar(ProgressStore.GetStar(CampaignRun.champion.championName));

            // M1/M2: khôi phục ẢI theo tên → độ khó sao (đảm bảo có ải tự sinh để resolve)
            AdventureFactory.EnsureAdventures(campaign);
            if (!string.IsNullOrEmpty(dto.adventureName) && campaign.adventures != null)
                foreach (var a in campaign.adventures)
                    if (a != null && a.adventureName == dto.adventureName)
                    { CampaignRun.adventure = a; CampaignRun.starDifficulty = a.starDifficulty; break; }

            // powers (dữ liệu thuần → add thẳng)
            foreach (var p in dto.powers) CampaignRun.Add(p);
            if (dto.enemyPowers != null)
                foreach (var p in dto.enemyPowers) CampaignRun.AddEnemyPower(p);

            // relics theo tên
            if (campaign.relicPool != null)
                foreach (var name in dto.relicNames)
                    foreach (var r in campaign.relicPool)
                        if (r != null && r.relicName == name) { CampaignRun.AddRelic(r); break; }

            // powers theo tên (Lõi vận hành)
            if (campaign.powerCores != null && dto.powerNames != null)
                foreach (var name in dto.powerNames)
                    foreach (var p in campaign.powerCores)
                        if (p != null && p.powerName == name) { CampaignRun.AddPower(p); break; }

            // deck theo tên
            foreach (var cn in dto.deckCardNames)
            {
                var cd = ResolveCard(cn, campaign, library);
                if (cd != null) CampaignRun.AddCardToDeck(cd);
            }
            // fallback: deck rỗng mà có champion → dựng lại (15 lá distinct + nâng bài theo cấp)
            if (CampaignRun.RunDeck.Count == 0 && CampaignRun.champion != null)
                CampaignRun.RebuildRunDeck(CampaignRun.champion);

            CampaignRun.gold = dto.gold;
            CampaignRun.difficulty = dto.difficulty <= 0 ? 1 : dto.difficulty;
            CampaignRun.runStartXp = dto.runStartXp;
            CampaignRun.runStartEssence = dto.runStartEssence;
            CampaignRun.runStartCores = dto.runStartCores;
            CampaignRun.nexusMax = dto.nexusMax;
            CampaignRun.nexusCurrent = dto.nexusCurrent;
            CampaignRun.nexusInit = dto.nexusInit;

            CampaignContext.campaign = campaign;
            CampaignContext.map = dto.map;
            CampaignContext.currentNodeId = dto.currentNodeId;
            CampaignContext.pendingRelic = dto.pendingRelic;
            CampaignContext.levelIndex = dto.levelIndex;

            // Khôi phục item NHẶT (RunItems) từ save, rồi re-derive item TRANG BỊ lá champion (có icon).
            LoRClone.Model.RunItems.Restore(dto.items);
            CampaignRun.RefreshChampionItems();

            Debug.Log("[RunSaveStore] Đã khôi phục hành trình đang dở.");
            return true;
        }

        static CardData ResolveCard(string cardName, CampaignData campaign, CardLibrary library)
        {
            if (string.IsNullOrEmpty(cardName)) return null;

            if (library != null && library.allCards != null)
                foreach (var c in library.allCards)
                    if (c != null && c.cardName == cardName) return c;

            if (campaign != null)
            {
                if (campaign.starterCards != null)
                    foreach (var c in campaign.starterCards) if (c != null && c.cardName == cardName) return c;
                if (campaign.champions != null)
                    foreach (var ch in campaign.champions)
                        if (ch != null && ch.starterDeck != null && ch.starterDeck.cards != null)
                            foreach (var c in ch.starterDeck.cards) if (c != null && c.cardName == cardName) return c;
                if (campaign.levels != null)
                    foreach (var lv in campaign.levels)
                        if (lv != null && lv.rewardCards != null)
                            foreach (var c in lv.rewardCards) if (c != null && c.cardName == cardName) return c;
            }
            return null;
        }
    }
}