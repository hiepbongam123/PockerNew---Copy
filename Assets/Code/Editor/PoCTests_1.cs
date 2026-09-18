// ─────────────────────────────────────────────────────────────────────────────
// PoCTests.cs — Bộ UNIT TEST (EditMode) cho hệ PoC, khớp Test Plan (Spec mục 6.2).
//
// PHẠM VI (chạy được, KHÔNG cần scene/GameController):
//   A. AdventureFactory  (A1–A6)      — ladder ải
//   B. AdventureProgress (B1–B6)      — tiến độ per-champion (PlayerPrefs)
//   C. ChestSystem       (C1–C2, C5)  — RollTier + tên/hex bậc
//   D. EncounterModifier (D1–D5)      — cờ & mô tả
//   E. CardItem/Library  (E1–E6)      — RandomOf/ToRuntime/RunItems
//
// Các test đánh ⚪ "blocked" trong Spec (cần ProgressStore/CampaignRun/GameController/UI)
// KHÔNG nằm ở đây — sẽ bổ sung khi có đủ dependency.
//
// SETUP TRONG UNITY (chỉ 1 lần):
//   1. Đặt file này trong 1 folder, VD Assets/Tests/EditMode/PoCTests.cs
//   2. Tạo Assembly Definition cho folder đó (Create → Assembly Definition), tick "Editor" +
//      "Test Assemblies", và REFERENCE tới assembly chứa code game (LoRClone.*) + nunit.
//   3. Window → General → Test Runner → tab EditMode → Run All.
//
// LƯU Ý: test có đụng PlayerPrefs (AdventureProgress) — đã Reset trong [SetUp]/[TearDown]
//        để không dây bẩn save thật.
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Tests
{
    // ══════════════════════════════════════════════════════════════════
    // A. AdventureFactory — Ladder ải
    // ══════════════════════════════════════════════════════════════════
    public class AdventureFactoryTests
    {
        static CampaignData NewCampaign() => ScriptableObject.CreateInstance<CampaignData>();

        [Test] // A1
        public void A1_EmptyAdventures_BuildsLadderOf21()
        {
            var camp = NewCampaign();
            camp.adventures = new List<AdventureData>();
            AdventureFactory.EnsureAdventures(camp);
            Assert.AreEqual(21, camp.adventures.Count, "Ladder phải có 21 ải (0★→10★, bước 0.5).");
            Assert.AreEqual(0f, camp.adventures[0].starDifficulty, 0.001f, "Ải đầu = 0★.");
            Assert.AreEqual(10f, camp.adventures[20].starDifficulty, 0.001f, "Ải cuối = 10★.");
        }

        [Test] // A2
        public void A2_LadderExists_NotRebuilt()
        {
            var camp = NewCampaign();
            var zero = ScriptableObject.CreateInstance<AdventureData>();
            zero.starDifficulty = 0f; zero.adventureName = "Ải 0 Sao";
            camp.adventures = new List<AdventureData> { zero };
            AdventureFactory.EnsureAdventures(camp);
            Assert.AreEqual(1, camp.adventures.Count, "Đã có ải 0★ → nhận là ladder → KHÔNG dựng lại.");
            Assert.AreSame(zero, camp.adventures[0], "Giữ nguyên phần tử cũ.");
        }

        [Test] // A3
        public void A3_ThreeOldAdventuresNoZeroStar_Rebuilt()
        {
            var camp = NewCampaign();
            camp.adventures = new List<AdventureData>();
            for (int i = 0; i < 3; i++)
            {
                var a = ScriptableObject.CreateInstance<AdventureData>();
                a.starDifficulty = 1f + i;   // không có ải ~0★
                camp.adventures.Add(a);
            }
            AdventureFactory.EnsureAdventures(camp);
            Assert.AreEqual(21, camp.adventures.Count, "Không có ải 0★ → dựng lại thành 21 ải ladder.");
        }

        [Test] // A4
        public void A4_LadderFields_Correct()
        {
            var camp = NewCampaign();
            camp.adventures = new List<AdventureData>();
            AdventureFactory.EnsureAdventures(camp);
            for (int i = 0; i < camp.adventures.Count; i++)
            {
                var a = camp.adventures[i];
                Assert.AreEqual(12, a.columns, $"[i={i}] columns==12.");
                Assert.AreEqual(1, a.requiredChampionStar, $"[i={i}] requiredChampionStar==1.");
                if (i == 0) Assert.IsNull(a.prerequisite, "Ải đầu không có prerequisite.");
                else Assert.AreSame(camp.adventures[i - 1], a.prerequisite, $"[i={i}] prerequisite == ải trước.");
            }
        }

        [Test] // A5
        public void A5_RewardMultiplier_10Star()
        {
            var camp = NewCampaign();
            camp.adventures = new List<AdventureData>();
            AdventureFactory.EnsureAdventures(camp);
            Assert.AreEqual(2.5f, camp.adventures[20].rewardMultiplier, 0.01f, "10★: 1 + 10*0.15 = 2.5.");
        }

        [Test] // A6
        public void A6_RegionRotation_Every9()
        {
            var camp = NewCampaign();
            camp.adventures = new List<AdventureData>();
            AdventureFactory.EnsureAdventures(camp);
            // region luân phiên 9 vùng (i % 9) → cùng dư 9 phải cùng region.
            Assert.AreEqual(camp.adventures[0].region, camp.adventures[9].region, "i=0 và i=9 cùng vùng.");
            Assert.AreEqual(camp.adventures[9].region, camp.adventures[18].region, "i=9 và i=18 cùng vùng.");
            Assert.AreNotEqual(camp.adventures[0].region, camp.adventures[1].region, "Vùng kế tiếp phải khác.");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // B. AdventureProgress — tiến độ per-champion (PlayerPrefs)
    // ══════════════════════════════════════════════════════════════════
    public class AdventureProgressTests
    {
        [SetUp] public void SetUp() => AdventureProgress.Reset();
        [TearDown] public void TearDown() => AdventureProgress.Reset();

        [Test] // B1
        public void B1_Clean_IsCompletedFalse()
            => Assert.IsFalse(AdventureProgress.IsCompleted("Ashe", "Ải 1"));

        [Test] // B2
        public void B2_MarkFirstTime_ReturnsTrue()
            => Assert.IsTrue(AdventureProgress.MarkCompleted("Ashe", "Ải 1"), "Lần đầu → true.");

        [Test] // B3
        public void B3_MarkSecondTime_ReturnsFalse()
        {
            AdventureProgress.MarkCompleted("Ashe", "Ải 1");
            Assert.IsFalse(AdventureProgress.MarkCompleted("Ashe", "Ải 1"), "Lần 2 → false.");
        }

        [Test] // B4
        public void B4_DifferentChampion_NotCompleted()
        {
            AdventureProgress.MarkCompleted("Ashe", "Ải 1");
            Assert.IsFalse(AdventureProgress.IsCompleted("Garen", "Ải 1"), "Khác champion → chưa hoàn thành.");
        }

        [Test] // B5 (phần persist: kiểm IsCompleted sau khi mark — PlayerPrefs đã ghi)
        public void B5_MarkedStaysCompleted()
        {
            AdventureProgress.MarkCompleted("Ashe", "Ải 1");
            Assert.IsTrue(AdventureProgress.IsCompleted("Ashe", "Ải 1"), "Đã mark → persist trong PlayerPrefs.");
        }

        [Test] // B6
        public void B6_EmptyName_ReturnsFalse()
            => Assert.IsFalse(AdventureProgress.MarkCompleted("Ashe", ""), "Tên rỗng → không lưu, trả false.");
    }

    // ══════════════════════════════════════════════════════════════════
    // C. ChestSystem — RollTier + tên/hex bậc
    // ══════════════════════════════════════════════════════════════════
    public class ChestSystemTests
    {
        [Test] // C1
        public void C1_LowStar_NeverLegend()
        {
            for (int i = 0; i < 10000; i++)
                Assert.AreNotEqual(ChestTier.Legend, ChestSystem.RollTier(1f), "≤2★: trọng số Legend = 0.");
        }

        [Test] // C2
        public void C2_HighStar_DistributionRoughlyMatches()
        {
            const int N = 20000;
            var count = new int[4];
            for (int i = 0; i < N; i++) count[(int)ChestSystem.RollTier(10f)]++;
            float[] pct = { 100f * count[0] / N, 100f * count[1] / N, 100f * count[2] / N, 100f * count[3] / N };
            float[] exp = { 10f, 33f, 40f, 17f };   // >7★ {Thường,Hiếm,Tuyệt,Huyền}
            for (int t = 0; t < 4; t++)
                Assert.AreEqual(exp[t], pct[t], 5f, $"Bậc {t}: {pct[t]:0.#}% (kỳ vọng ~{exp[t]}%).");
        }

        [Test] // C5
        public void C5_TierNameHex_AllFourValid()
        {
            foreach (ChestTier t in System.Enum.GetValues(typeof(ChestTier)))
            {
                Assert.IsFalse(string.IsNullOrEmpty(ChestSystem.TierName(t)), $"TierName({t}) không rỗng.");
                Assert.IsFalse(string.IsNullOrEmpty(ChestSystem.TierHex(t)), $"TierHex({t}) không rỗng.");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // D. EncounterModifier — cờ & mô tả
    // ══════════════════════════════════════════════════════════════════
    public class EncounterModifierTests
    {
        static EncounterModifier New() => ScriptableObject.CreateInstance<EncounterModifier>();

        [Test] // D1
        public void D1_OnlyNexusBonus_HasOneTimeOnly()
        {
            var m = New(); m.enemyNexusBonus = 12;
            Assert.IsTrue(m.HasOneTime);
            Assert.IsFalse(m.HasPerRound);
            Assert.IsFalse(m.HasBossPower);
        }

        [Test] // D2
        public void D2_BossKeyword_HasBossPower()
        {
            var m = New(); m.bossGrantKeyword = true; m.bossKeyword = KeywordType.Regeneration;
            Assert.IsTrue(m.HasBossPower);
            Assert.IsFalse(string.IsNullOrEmpty(m.BossPowerText()), "BossPowerText có nội dung.");
        }

        [Test] // D3
        public void D3_DrawPerRound_HasPerRound()
        {
            var m = New(); m.enemyDrawPerRound = 1;
            Assert.IsTrue(m.HasPerRound);
        }

        [Test] // D4
        public void D4_DescriptionSet_AutoDescribeReturnsIt()
        {
            var m = New(); m.description = "Mô tả tay";
            Assert.AreEqual("Mô tả tay", m.AutoDescribe());
        }

        [Test] // D5
        public void D5_Empty_AutoDescribePlaceholder()
        {
            var m = New();
            Assert.AreEqual("(chưa cấu hình)", m.AutoDescribe());
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // E. CardItem / Library — RandomOf / ToRuntime / RunItems
    // ══════════════════════════════════════════════════════════════════
    public class CardItemTests
    {
        [SetUp] public void SetUp() { CardItem.SetLibrary(null); RunItems.ClearRun(); }
        [TearDown] public void TearDown() { CardItem.SetLibrary(null); RunItems.ClearRun(); }

        static CardItemLibrary Lib(params CardItemDef[] defs)
        {
            var lib = ScriptableObject.CreateInstance<CardItemLibrary>();
            lib.items = new List<CardItemDef>(defs);
            return lib;
        }

        static CardItemDef Def(string name, ItemRarity r, int atk = 0, int hp = 0)
            => new CardItemDef { itemName = name, rarity = r, atkBonus = atk, hpBonus = hp };

        [Test] // E1
        public void E1_NullLibrary_RandomOfNull()
        {
            CardItem.SetLibrary(null);
            Assert.IsNull(CardItem.RandomOf(ItemRarity.Common), "Library trống → null (không crash).");
        }

        [Test] // E2
        public void E2_OneRare_RandomOfReturnsIt()
        {
            CardItem.SetLibrary(Lib(Def("Kiếm Hiếm", ItemRarity.Rare, 3, 0)));
            var it = CardItem.RandomOf(ItemRarity.Rare);
            Assert.IsNotNull(it);
            Assert.AreEqual("Kiếm Hiếm", it.itemName);
        }

        [Test] // E3
        public void E3_OnlyCommon_RandomOfEpicFallsBack()
        {
            CardItem.SetLibrary(Lib(Def("Búa Thường", ItemRarity.Common, 1, 1)));
            Assert.IsNotNull(CardItem.RandomOf(ItemRarity.Epic), "Không có Epic → fallback bốc từ toàn pool (không null).");
        }

        [Test] // E4
        public void E4_ToRuntime_FieldsMatch()
        {
            var d = new CardItemDef
            {
                itemName = "Giáp", rarity = ItemRarity.Epic,
                atkBonus = 2, hpBonus = 3, costReduction = 1,
                grantsKeyword = true, keyword = KeywordType.Barrier,
                championEquip = true, bonusMana = 1, bonusNexus = 4
            };
            var rt = d.ToRuntime();
            Assert.AreEqual("Giáp", rt.itemName);
            Assert.AreEqual(ItemRarity.Epic, rt.rarity);
            Assert.AreEqual(2, rt.atkBonus);
            Assert.AreEqual(3, rt.hpBonus);
            Assert.AreEqual(1, rt.costReduction);
            Assert.IsTrue(rt.grantKeyword.HasValue && rt.grantKeyword.Value == KeywordType.Barrier);
            Assert.IsTrue(rt.championEquip);
            Assert.AreEqual(1, rt.bonusMana);
            Assert.AreEqual(4, rt.bonusNexus);
        }

        [Test] // E5
        public void E5_AttachTwice_CountTwo()
        {
            RunItems.ClearRun();
            RunItems.Attach("Wolf", new CardItem("A", ItemRarity.Common, 1, 0));
            RunItems.Attach("Wolf", new CardItem("B", ItemRarity.Common, 0, 1));
            Assert.AreEqual(2, RunItems.CountOf("Wolf"));
        }

        [Test] // E6
        public void E6_SnapshotRestore_Preserves()
        {
            // cần library để Restore tra ngược (icon/skill) — item chỉ stat thì vẫn khôi phục count/tên.
            CardItem.SetLibrary(Lib(Def("Bùa", ItemRarity.Common, 2, 1)));
            RunItems.ClearRun();
            RunItems.Attach("Wolf", new CardItem("Bùa", ItemRarity.Common, 2, 1));
            var snap = RunItems.Snapshot();

            RunItems.ClearRun();
            Assert.AreEqual(0, RunItems.CountOf("Wolf"), "Đã xoá.");

            RunItems.Restore(snap);
            Assert.AreEqual(1, RunItems.CountOf("Wolf"), "Khôi phục đúng số item theo lá.");
            var items = RunItems.ItemsOf("Wolf");
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("Bùa", items[0].itemName);
            Assert.AreEqual(2, items[0].atkBonus);
        }
    }
}
