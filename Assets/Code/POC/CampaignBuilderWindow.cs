#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using LoRClone.Data;
using LoRClone.View; // LobbyManager

namespace LoRClone.Tools
{
    /// <summary>
    /// Cửa sổ Editor tự sinh CampaignData + LevelData mẫu (để test Leo Tháp nhanh).
    ///
    /// DÙNG: Menu trên cùng Unity → "LoRClone → Campaign Builder" → gán ref → bấm Build.
    /// ĐẶT FILE TRONG folder tên "Editor" (vd Assets/Editor/CampaignBuilderWindow.cs).
    ///
    /// Nếu menu "LoRClone" KHÔNG hiện → project đang có LỖI COMPILE (xem Console, sửa hết đã).
    /// </summary>
    public class CampaignBuilderWindow : EditorWindow
    {
        CardLibrary cardLibrary;
        LobbyManager lobby;
        string campaignName = "Con Đường Anh Hùng";
        int levelCount = 8;
        int maxCopiesPerCard = 3;
        string outputFolder = "Assets/Campaign_Generated";

        [MenuItem("LoRClone/Campaign Builder")]
        static void Open()
        {
            GetWindow<CampaignBuilderWindow>("Campaign Builder").minSize = new Vector2(360, 260);
        }

        // Bảng tuning mặc định (khớp LevelDesign_LeoThap.md).
        static readonly int[] EnemyHp   = { 8, 12, 15, 18, 20, 22, 24, 30 };
        static readonly int[] PlayerHp  = { 20, 20, 18, 20, 20, 18, 16, 15 };
        static readonly int[] StartMana = { 6, 4, 2, 0, 0, 0, 0, 0 };
        static readonly int[] DeckPick  = { 1, 2, 3, 4, 5, 6, 7, 8 };

        void OnGUI()
        {
            EditorGUILayout.LabelField("Tự sinh tháp Leo Tháp mẫu", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Cách 1: kéo LobbyManager vào (tự lấy CardLibrary + availableDecks).\n" +
                "Cách 2: gán CardLibrary trực tiếp.\n" +
                "Sinh xong: gán Campaign_Generated vào LobbyMenuView.campaign.",
                MessageType.Info);

            lobby = (LobbyManager)EditorGUILayout.ObjectField("Lobby Manager", lobby, typeof(LobbyManager), true);
            cardLibrary = (CardLibrary)EditorGUILayout.ObjectField("Card Library", cardLibrary, typeof(CardLibrary), false);
            campaignName = EditorGUILayout.TextField("Tên Campaign", campaignName);
            levelCount = Mathf.Max(1, EditorGUILayout.IntField("Số tầng", levelCount));
            maxCopiesPerCard = Mathf.Max(1, EditorGUILayout.IntField("Max bản/lá", maxCopiesPerCard));
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

            EditorGUILayout.Space();
            if (GUILayout.Button("Build Sample Campaign", GUILayout.Height(34)))
                Build();
        }

        void Build()
        {
            var lib = cardLibrary != null ? cardLibrary : (lobby != null ? lobby.cardLibrary : null);
            var decks = (lobby != null) ? lobby.availableDecks : null;

            var cards = CollectCards(lib, decks);
            if (cards.Count == 0)
            {
                EditorUtility.DisplayDialog("Campaign Builder",
                    "Không tìm thấy card nào.\nGán CardLibrary hoặc LobbyManager (có availableDecks).", "OK");
                return;
            }

            var units = cards.FindAll(c => c.cardType == CardType.Unit);
            units.Sort((a, b) => a.manaCost.CompareTo(b.manaCost));
            var spells = cards.FindAll(c => c.cardType == CardType.Spell);
            spells.Sort((a, b) => a.manaCost.CompareTo(b.manaCost));

            if (units.Count == 0)
            {
                EditorUtility.DisplayDialog("Campaign Builder", "Không tìm thấy Unit nào trong nguồn card.", "OK");
                return;
            }

            EnsureFolder(outputFolder);

            var campaign = ScriptableObject.CreateInstance<CampaignData>();
            campaign.campaignName = campaignName;
            campaign.maxCopiesPerCard = maxCopiesPerCard;
            campaign.starterCards = new List<CardData> { units[0] };
            campaign.levels = new List<LevelData>();

            int unitCursor = 1;
            int spellCursor = 0;

            for (int i = 0; i < levelCount; i++)
            {
                var lv = ScriptableObject.CreateInstance<LevelData>();
                lv.levelName = $"Tầng {i + 1}";
                lv.description = "Tầng tự sinh — chỉnh lại cho thú vị nhé.";
                lv.enemyHealth = Pick(EnemyHp, i, levelCount, 10, 30);
                lv.playerHealth = Pick(PlayerHp, i, levelCount, 20, 15);
                lv.playerStartMana = Pick(StartMana, i, levelCount, 6, 0);
                lv.enemyStartMana = 0;
                lv.deckPickCount = Mathf.Max(1, Pick(DeckPick, i, levelCount, 1, 8));

                lv.enemyDeck = (decks != null && decks.Length > 0)
                    ? decks[i % decks.Length]
                    : MakeTempEnemyDeck(units, i);

                var reward = new List<CardData>();
                if (i == 1 && spells.Count > 0)
                    reward.Add(spells[spellCursor++ % spells.Count]);
                else if (unitCursor < units.Count)
                    reward.Add(units[unitCursor++]);
                lv.rewardCards = reward;

                lv.startUnits = new List<PrePlacedUnit>();
                if (i == 0)
                    lv.startUnits.Add(new PrePlacedUnit { unit = units[0], onEnemySide = true, benchSlot = 0 });

                AssetDatabase.CreateAsset(lv, $"{outputFolder}/Level_{i + 1:00}.asset");
                campaign.levels.Add(lv);
            }

            AssetDatabase.CreateAsset(campaign, $"{outputFolder}/Campaign_Generated.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(campaign);
            Selection.activeObject = campaign;
            EditorUtility.DisplayDialog("Campaign Builder",
                $"Đã sinh {levelCount} tầng tại {outputFolder}.\n" +
                "Gán 'Campaign_Generated' vào LobbyMenuView.campaign để chơi thử.", "OK");
        }

        static List<CardData> CollectCards(CardLibrary lib, DeckData[] decks)
        {
            var list = new List<CardData>();
            var seen = new HashSet<CardData>();
            void Add(CardData c) { if (c != null && !c.isGenerated && seen.Add(c)) list.Add(c); }
            if (lib != null && lib.allCards != null)
                foreach (var c in lib.allCards) Add(c);
            if (decks != null)
                foreach (var d in decks)
                    if (d != null && d.cards != null)
                        foreach (var c in d.cards) Add(c);
            return list;
        }

        static DeckData MakeTempEnemyDeck(List<CardData> units, int levelIndex)
        {
            var deck = ScriptableObject.CreateInstance<DeckData>();
            deck.deckName = $"EnemyDeck_T{levelIndex + 1}";
            deck.cards = new List<CardData>();
            int start = Mathf.Clamp(levelIndex, 0, Mathf.Max(0, units.Count - 4));
            for (int k = 0; k < 8 && start + (k % 4) < units.Count; k++)
                deck.cards.Add(units[start + (k % 4)]);
            AssetDatabase.CreateAsset(deck, $"Assets/Campaign_Generated/EnemyDeck_T{levelIndex + 1}.asset");
            return deck;
        }

        static int Pick(int[] table, int i, int total, int first, int last)
        {
            if (i < table.Length && total <= table.Length) return table[i];
            float t = total <= 1 ? 0f : (float)i / (total - 1);
            return Mathf.RoundToInt(Mathf.Lerp(first, last, t));
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, leaf);
        }
    }
}
#endif
