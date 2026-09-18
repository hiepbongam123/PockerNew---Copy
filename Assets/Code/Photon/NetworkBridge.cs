using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using LoRClone.Controller;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Net
{
    /// <summary>
    /// FILE 5/5 — Trung tâm mạng LOCKSTEP. CHỈ THÊM SAU KHI Photon (PUN2) import xong.
    /// Đặt lên 1 GameObject CÓ PhotonView trong SCENE GAME. Đặt file ở: Assets/Code/Net/NetworkBridge.cs
    ///
    /// Mô hình: mỗi máy coi "mình = player (dưới)". Host & client là ảnh gương.
    /// - Handshake: trao tên deck + seed → 2 máy StartNetworkGame giống hệt.
    /// - Relay: nước local (byPlayer=true) → gửi → máy kia áp byPlayer=false.
    /// - Desync: host gửi hash state, client so → lệch thì log.
    ///
    /// ★ THÊM (unit-skill target): relay lựa chọn target của unit-skill theo netId của UNIT
    ///   (lá skill sinh runtime netId=-1 nên không relay theo lá skill được). Máy kia CHỜ gói này
    ///   trong StageUnitSkillSpell/StageBattleSkillsCo rồi mới commit → 2 máy đánh cùng target.
    ///
    /// ★ THÊM (PvP-RPG): handshake trao thêm LOADOUT champion 2 phe (JSON) → StartNetworkGame áp
    ///   buff giống hệt 2 máy. Không có loadout → PvP thường (không đổi hành vi cũ).
    ///
    /// YÊU CẦU: các DeckData phải nằm trong Assets/Resources/Decks/ (để load theo tên qua mạng).
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class NetworkBridge : MonoBehaviourPun
    {
        public static NetworkBridge Instance;

        [Header("Deck resolve — GÁN ASSET CHUNG, GIỐNG NHAU Ở CẢ 2 BUILD")]
        [Tooltip("CardLibrary để resolve cardName → CardData khi dựng lại deck nhận qua mạng.")]
        public LoRClone.Data.CardLibrary cardLibrary;
        [Tooltip("Fallback resolve: gom card từ các deck preset này nếu cardLibrary trống.\n" +
                 "Nên gán = các DeckData preset để deck preset cũng đi qua đường cardName được.")]
        public DeckData[] fallbackDecks;

        [Header("PvP-RPG (Đấu Trường Anh Hùng) — GÁN ĐỂ RESOLVE CHAMPION")]
        [Tooltip("CampaignData để map championName → ChampionData → championCard khi áp loadout.\n" +
                 "Để trống = PvP thường (không loadout).")]
        public LoRClone.Data.CampaignData campaign;

        bool _started;
        bool LocalIsHost => PhotonNetwork.IsMasterClient;
        DeckData _localDeck;
        string _clientDeckName; // host lưu tên deck client gửi tới

        // Deck local DẠNG cardName (từ PvpDeckSelection; fallback: deck preset). Đây là dữ liệu trao qua mạng.
        List<string> _localCards;
        string _localName;
        string[] _clientCards;  // host lưu cards client gửi tới

        // ★ PvP-RPG: loadout champion local (JSON) — "" nếu PvP thường.
        string _localLoadoutJson = "";

        void Awake() { Instance = this; }

        void Start() => StartCoroutine(Boot());

        IEnumerator Boot()
        {
            // PvE: KHÔNG ở trong phòng Photon → bridge nghỉ hẳn, tránh spam RPC "not connected".
            if (!PhotonNetwork.InRoom)
            {
                Debug.Log("[PvP] Không ở phòng Photon → đây là PvE. NetworkBridge tắt.");
                enabled = false;
                yield break;
            }

            Debug.Log("[PvP] 1. NetworkBridge Boot — đợi GameController...");
            // đợi GameController tồn tại (scene game vừa load)
            while (GameController.Instance == null) yield return null;
            var gc = GameController.Instance;

            // Deck local = deck NGƯỜI CHƠI ĐÃ CHỌN (PvpDeckSelection). Fallback: deck preset gán Inspector.
            ResolveLocalDeck(gc);
            Debug.Log($"[PvP] 2. GameController sẵn sàng. LocalIsHost={LocalIsHost}, localDeck='{_localName}' ({(_localCards != null ? _localCards.Count : 0)} lá)");
            if (_localCards == null || _localCards.Count == 0)
                Debug.LogError("[PvP] ✖ localCards RỖNG — chưa chọn deck PvP (PvpDeckSelection) và cũng không có playerDeckData. Handshake sẽ đứng.");

            gc.OnLocalActionForNetwork += SendAction;
            gc.OnLocalMulliganForNetwork += SendMulligan;
            gc.OnLocalSpellTargetsForNetwork += SendSpellTargets;
            gc.OnLocalUnitSkillTargetsForNetwork += SendUnitSkillTargets; // ★ target unit-skill
            gc.OnLocalConcedeForNetwork += SendConcede;                   // ★ đầu hàng
            if (LocalIsHost) gc.OnStateChanged += _ => ScheduleHash(); // host so hash SAU khi state lắng (debounce)

            // Client gửi tên deck cho host, lặp lại tới khi trận bắt đầu (phòng RPC tới sớm).
            bool logged = false;
            while (!_started)
            {
                if (!PhotonNetwork.InRoom) yield break; // rời phòng giữa chừng → dừng, không RPC lỗi
                if (!LocalIsHost && _localCards != null && _localCards.Count > 0)
                {
                    if (!logged) { Debug.Log($"[PvP] 3. Client gửi deck '{_localName}' ({_localCards.Count} lá) cho host (lặp tới khi start)..."); logged = true; }
                    photonView.RPC(nameof(RpcClientDeck), RpcTarget.MasterClient, _localName, _localCards.ToArray(), _localLoadoutJson);
                }
                yield return new WaitForSeconds(0.5f);
            }
        }

        // ── HANDSHAKE ────────────────────────────────────────────────
        bool _hostBroadcasted;   // chống broadcast RpcStart nhiều lần (client gửi deck lặp mỗi 0.5s)

        [PunRPC]
        void RpcClientDeck(string clientDeckName, string[] clientCards, string clientLoadoutJson)   // chạy trên HOST
        {
            if (_started || _hostBroadcasted || !LocalIsHost) return;
            _clientDeckName = clientDeckName;
            _clientCards = clientCards;
            if (_localCards == null || _localCards.Count == 0)
            { Debug.LogError("[PvP] ✖ 4. Host nhận deck client nhưng host localCards RỖNG!"); return; }
            if (clientCards == null || clientCards.Length == 0)
            { Debug.LogError("[PvP] ✖ 4. Client gửi deck rỗng!"); return; }

            _hostBroadcasted = true;   // chỉ broadcast 1 lần
            int seed = Random.Range(int.MinValue, int.MaxValue);
            Debug.Log($"[PvP] 4. Host nhận deck client '{clientDeckName}' ({clientCards.Length} lá), seed={seed}, broadcast RpcStart.");
            // Trao TOÀN BỘ cardName của cả 2 deck (theo thứ tự) + loadout 2 phe → 2 máy dựng lại y hệt.
            photonView.RPC(nameof(RpcStart), RpcTarget.All,
                _localName, _localCards.ToArray(), _localLoadoutJson,
                clientDeckName, clientCards, clientLoadoutJson, seed);
        }

        [PunRPC]
        void RpcStart(string hostDeckName, string[] hostCards, string hostLoadoutJson,
                      string clientDeckName, string[] clientCards, string clientLoadoutJson, int seed)  // chạy trên CẢ 2
        {
            Debug.Log($"[PvP] 5. RpcStart: host='{hostDeckName}'({(hostCards != null ? hostCards.Length : 0)}), client='{clientDeckName}'({(clientCards != null ? clientCards.Length : 0)}), seed={seed}. LocalIsHost={LocalIsHost}");
            if (_started) return;
            _started = true;

            // Dựng lại DeckData từ cardName (KHÔNG load asset) → deck tự tạo cũng chạy được,
            // và 2 máy build GIỐNG HỆT (cùng resolver CardLibrary + cùng thứ tự) → không desync.
            var hostDeck = BuildDeckFromCards(hostDeckName, hostCards);
            var clientDeck = BuildDeckFromCards(clientDeckName, clientCards);
            Debug.Log($"[PvP] 6. Build deck từ cardName: host={hostDeck.cards.Count} lá, client={clientDeck.cards.Count} lá.");
            if (hostDeck.cards.Count == 0 || clientDeck.cards.Count == 0)
            {
                Debug.LogError("[PvP] ✖ 6. Deck build ra RỖNG — CardLibrary/fallbackDecks trên NetworkBridge chưa gán, hoặc thiếu card. 2 máy PHẢI có CardLibrary giống nhau!");
                return;
            }

            // Lưu chữ ký nội dung deck (tên + thuộc tính ảnh hưởng mô phỏng) để so 2 máy — bắt "lệch data build".
            _sigPlayer = DeckSig(hostDeck);
            _sigEnemy = DeckSig(clientDeck);

            // CANONICAL: cả 2 máy player=hostDeck (seed), enemy=clientDeck (seed+1) — GIỐNG HỆT nhau.
            GameController.NetLocalIsPlayer = LocalIsHost; // host điều khiển player, client điều khiển enemy

            // ★ PvP-RPG: dựng MatchContext (loadout 2 phe) TRƯỚC khi StartNetworkGame áp buff.
            SetupMatchContext(hostLoadoutJson, clientLoadoutJson);

            GameController.Instance.StartNetworkGame(hostDeck, clientDeck, seed, seed + 1);

            Debug.Log($"[PvP] 7. ✔ BẮT ĐẦU TRẬN. NetLocalIsPlayer={GameController.NetLocalIsPlayer}, seed={seed}, mode={MatchContext.mode}");

            // HOST gửi chữ ký deck → CLIENT so từng lá → phát hiện NGAY "2 build khác data card".
            if (LocalIsHost)
                photonView.RPC(nameof(RpcDeckCheck), RpcTarget.Others, _sigPlayer, _sigEnemy);
        }

        // ── PvP-RPG: dựng MatchContext từ loadout nhận qua mạng — GIỐNG HỆT 2 máy (canonical player=host) ──
        void SetupMatchContext(string hostLoadoutJson, string clientLoadoutJson)
        {
            // Thiếu loadout 1 trong 2 phe → coi là PvP THƯỜNG (không áp buff).
            if (string.IsNullOrEmpty(hostLoadoutJson) || string.IsNullOrEmpty(clientLoadoutJson))
            {
                MatchContext.mode = MatchMode.PvP_Normal;
                return;
            }
            MatchContext.mode = MatchMode.PvP_Gauntlet;
            MatchContext.playerLoadout = ChampionLoadout.FromJson(hostLoadoutJson);   // player = host (canonical)
            MatchContext.enemyLoadout = ChampionLoadout.FromJson(clientLoadoutJson);

            // Áp thư viện trang bị để resolve icon/mô tả/skill trang bị. Asset GIỐNG NHAU 2 máy (cùng build).
            // FIX: trước lấy THẲNG field 'campaign' (gán Inspector) → máy nào QUÊN gán → library null →
            //      MẤT icon + skill trang bị. Giờ ResolveItemLibrary tìm cả trong Resources LẪN asset đã nạp
            //      trong bộ nhớ (FindObjectsOfTypeAll) → không cần nhớ gán campaign hay để trong Resources.
            var camp = ResolveCampaignAsset(MatchContext.playerLoadout.championName, MatchContext.enemyLoadout.championName);
            LoRClone.Model.CardItem.SetLibrary(ResolveItemLibrary(camp));

            MatchContext.playerChampionCard = ResolveChampionCard(camp, MatchContext.playerLoadout.championName);
            MatchContext.enemyChampionCard = ResolveChampionCard(camp, MatchContext.enemyLoadout.championName);
        }

        // ── Resolve CampaignData: ưu tiên field Inspector; NULL → fallback Resources (tất định 2 máy) ──
        //    Vì sao: 'campaign' là field gán tay từng máy → dễ quên ở client → mất library. Fallback này
        //    nạp CampaignData từ Resources, chọn ƯU TIÊN campaign CHỨA champion trong trận, else theo tên Ordinal.
        LoRClone.Data.CampaignData _resolvedCampaign;
        LoRClone.Data.CampaignData ResolveCampaignAsset(string champA, string champB)
        {
            if (campaign != null) return campaign;             // gán Inspector → dùng luôn
            if (_resolvedCampaign != null) return _resolvedCampaign;

            var all = Resources.LoadAll<LoRClone.Data.CampaignData>("");   // toàn bộ CampaignData dưới Resources/
            LoRClone.Data.CampaignData best = null;
            if (all != null)
                foreach (var c in all)
                {
                    if (c == null) continue;
                    bool hasChamp = ContainsChampion(c, champA) || ContainsChampion(c, champB);
                    bool bestHas = ContainsChampion(best, champA) || ContainsChampion(best, champB);
                    if (best == null
                        || (hasChamp && !bestHas)                                       // ưu tiên campaign đúng
                        || (hasChamp == bestHas && string.CompareOrdinal(c.name, best.name) < 0)) // tie → tên nhỏ nhất (deterministic)
                        best = c;
                }
            // Phòng asset chưa nằm dưới Resources nhưng đã nạp sẵn trong bộ nhớ.
            if (best == null)
            {
                var loaded = Resources.FindObjectsOfTypeAll<LoRClone.Data.CampaignData>();
                if (loaded != null)
                    foreach (var c in loaded)
                        if (c != null && (best == null || string.CompareOrdinal(c.name, best.name) < 0)) best = c;
            }
            _resolvedCampaign = best;
            if (best == null)
                Debug.LogWarning("[PvP] ✖ Không tìm thấy CampaignData (chưa gán NetworkBridge.campaign & không có trong Resources) → " +
                                 "mất icon/skill/panel trang bị. Gán 'campaign' HOẶC để CampaignData trong thư mục Resources.");
            return best;
        }

        // ── Resolve CardItemLibrary (cho icon/mô tả/skill trang bị) — KHÔNG phụ thuộc gán Inspector ──
        //    Thứ tự: (1) library của campaign đã resolve; (2) Resources; (3) asset ĐÃ NẠP trong bộ nhớ
        //    (FindObjectsOfTypeAll — bắt được library mà hub PoC đang tham chiếu, dù không ở Resources).
        //    Ưu tiên library CÓ item (bỏ qua library rỗng). DISPLAY-only nên chọn thế nào cũng an toàn.
        static LoRClone.Data.CardItemLibrary _resolvedLib;
        LoRClone.Data.CardItemLibrary ResolveItemLibrary(LoRClone.Data.CampaignData camp)
        {
            // (0) Library GIỮ SỐNG từ lúc mở Đấu Trường — nguồn chắc ăn nhất: asset còn nạp, icon còn sống.
            if (GauntletEntry.Library != null && HasItems(GauntletEntry.Library)) return GauntletEntry.Library;

            if (camp != null && camp.cardItemLibrary != null && HasItems(camp.cardItemLibrary)) return camp.cardItemLibrary;
            if (_resolvedLib != null) return _resolvedLib;

            LoRClone.Data.CardItemLibrary best = camp != null ? camp.cardItemLibrary : null;
            PickBetterLib(ref best, Resources.LoadAll<LoRClone.Data.CardItemLibrary>(""));
            PickBetterLib(ref best, Resources.FindObjectsOfTypeAll<LoRClone.Data.CardItemLibrary>());

            _resolvedLib = best;
            if (best == null || !HasItems(best))
                Debug.LogWarning("[PvP] ✖ Không tìm thấy CardItemLibrary có item → mất icon/tên trang bị. " +
                                 "Gán NetworkBridge.campaign, hoặc để CardItemLibrary trong build (được tham chiếu bởi CampaignData).");
            return best;
        }

        static bool HasItems(LoRClone.Data.CardItemLibrary l) => l != null && l.items != null && l.items.Count > 0;

        static void PickBetterLib(ref LoRClone.Data.CardItemLibrary best, LoRClone.Data.CardItemLibrary[] cands)
        {
            if (cands == null) return;
            foreach (var l in cands)
            {
                if (l == null) continue;
                bool lHas = HasItems(l), bHas = HasItems(best);
                if (best == null
                    || (lHas && !bHas)                                                 // ưu tiên library có item
                    || (lHas == bHas && string.CompareOrdinal(l.name, best.name) < 0)) // tie → tên nhỏ nhất (ổn định)
                    best = l;
            }
        }

        static bool ContainsChampion(LoRClone.Data.CampaignData c, string championName)
        {
            if (c == null || c.champions == null || string.IsNullOrEmpty(championName)) return false;
            foreach (var ch in c.champions)
                if (ch != null && ch.championName == championName) return true;
            return false;
        }

        // championName → championCard (qua CampaignData.champions, asset chung 2 máy).
        CardData ResolveChampionCard(LoRClone.Data.CampaignData camp, string championName)
        {
            if (camp == null || camp.champions == null || string.IsNullOrEmpty(championName)) return null;
            foreach (var ch in camp.champions)
                if (ch != null && ch.championName == championName) return ch.championCard;
            return null;
        }

        // ══════════ RÀO TRƯỚC: KIỂM TRA DATA CARD 2 MÁY (lockstep cần data giống hệt) ══════════
        // Chữ ký MỖI lá = tên + các field ảnh hưởng MÔ PHỎNG. Nếu 2 build khác nhau 1 field (VD
        // guaranteedInOpeningHand, chỉ số, cost, loại, tốc độ phép...) → in ra ĐÚNG lá + field lệch,
        // thay vì để desync khó đọc giữa trận.
        string[] _sigPlayer, _sigEnemy;

        static string CardSig(LoRClone.Data.CardData c)
        {
            if (c == null) return "∅";
            // name #type / cost / atk / hp / guaranteed / levelUp / spellSpeed / generated
            return $"{c.cardName}#t{(int)c.cardType}m{c.manaCost}a{c.baseAttack}h{c.baseHealth}" +
                   $"g{(c.guaranteedInOpeningHand ? 1 : 0)}l{(c.canLevelUp ? 1 : 0)}s{(int)c.spellSpeed}x{(c.isGenerated ? 1 : 0)}";
        }

        static string[] DeckSig(DeckData d)
        {
            int n = d != null && d.cards != null ? d.cards.Count : 0;
            var arr = new string[n];
            for (int i = 0; i < n; i++) arr[i] = CardSig(d.cards[i]);
            return arr;
        }

        [PunRPC]
        void RpcDeckCheck(string[] hostSigPlayer, string[] hostSigEnemy)   // chạy trên CLIENT
        {
            CompareSig("DECK NGƯỜI (host)", hostSigPlayer, _sigPlayer);
            CompareSig("DECK ĐỊCH (client)", hostSigEnemy, _sigEnemy);
        }

        static void CompareSig(string label, string[] host, string[] mine)
        {
            if (host == null) host = new string[0];
            if (mine == null) mine = new string[0];
            if (host.Length != mine.Length)
            {
                Debug.LogError($"[PvP] ✖ DATA LỆCH ({label}): số lá KHÁC NHAU — host {host.Length} vs máy này {mine.Length}. " +
                               "→ 2 build KHÔNG cùng bộ card (thiếu/thừa lá). Phải dùng CHUNG 1 build.");
                return;
            }
            int diff = 0;
            int n = host.Length;
            for (int i = 0; i < n; i++)
                if (host[i] != mine[i])
                {
                    if (diff < 8)
                        Debug.LogError($"[PvP] ✖ DATA LỆCH ({label}) lá #{i}: host='{host[i]}'  ≠  máy này='{mine[i]}'  " +
                                       "→ thường do 2 lá TRÙNG cardName (lá gốc vs levelUpForm/token) resolve khác nhau, " +
                                       "hoặc 2 build khác data. Đặt cardName DUY NHẤT cho mỗi lá là chắc nhất.");
                    diff++;
                }
            if (diff == 0) Debug.Log($"[PvP] ✔ DATA khớp ({label}): {n} lá giống hệt 2 máy.");
            else Debug.LogError($"[PvP] ✖ Tổng cộng {diff} lá lệch data ở {label} — đây là NGUYÊN NHÂN desync. Đồng bộ CardData 2 build!");
        }

        // ── Đọc deck local đã chọn (PvpDeckSelection) → cardName; fallback deck preset ──
        void ResolveLocalDeck(GameController gc)
        {
            if (LoRClone.Data.PvpDeckSelection.HasSelection)
            {
                _localName = LoRClone.Data.PvpDeckSelection.LocalDeckName;
                _localCards = new List<string>(LoRClone.Data.PvpDeckSelection.LocalDeckCards);
            }
            else
            {
                // Fallback (an toàn): deck preset gán Inspector → lấy cardName theo thứ tự.
                _localDeck = gc.playerDeckData;
                _localCards = new List<string>();
                if (_localDeck != null && _localDeck.cards != null)
                {
                    _localName = _localDeck.deckName;
                    foreach (var c in _localDeck.cards) if (c != null) _localCards.Add(c.cardName);
                    Debug.LogWarning("[PvP] Chưa chọn deck PvP (PvpDeckSelection) → dùng tạm deck preset gán Inspector.");
                }
            }

            // ★ PvP-RPG: đọc loadout local đã set ở lobby ("" nếu PvP thường).
            _localLoadoutJson = LoRClone.Data.PvpLoadoutSelection.LocalJson;
        }

        // ── Dựng DeckData runtime từ danh sách cardName (deterministic 2 máy) ──
        DeckData BuildDeckFromCards(string name, string[] cardNames)
        {
            // Nguồn resolve theo thứ tự ưu tiên (chống lỗi "deck rỗng" do quên gán):
            //   library: field NetworkBridge → GameConfig.cardLibrary (Lobby đẩy sang).
            //   fallback: field fallbackDecks + deck preset của GameController (playerDeckData/enemyDeckData).
            var det = DeterministicResolver();                 // NGUỒN CHÍNH — giống hệt 2 máy
            var lib = cardLibrary != null ? cardLibrary : LoRClone.GameConfig.cardLibrary;
            var resolver = CustomDeckStore.BuildResolver(lib, ResolveFallbackDecks()); // dự phòng
            var deck = ScriptableObject.CreateInstance<DeckData>();
            deck.deckName = string.IsNullOrEmpty(name) ? "PvP Deck" : name;
            deck.cards = new List<CardData>();
            int missing = 0;
            if (cardNames != null)
                foreach (var n in cardNames)
                {
                    CardData cd = null;
                    if (!det.TryGetValue(n, out cd) || cd == null)            // ưu tiên resolver deterministic
                        if (!resolver.TryGetValue(n, out cd)) cd = null;      // dự phòng nếu thiếu
                    if (cd != null) deck.cards.Add(cd);
                    else
                    {
                        missing++;
                        Debug.LogError($"[PvP] ✖ BuildDeck '{name}': KHÔNG tìm thấy card '{n}' trên máy này → bỏ qua.");
                    }
                }
            if (missing > 0)
                Debug.LogError($"[PvP] ✖ Deck '{name}': thiếu {missing}/{(cardNames != null ? cardNames.Length : 0)} lá trên máy này.");
            return deck;
        }

        // ══════════ RESOLVER DETERMINISTIC (giống hệt 2 máy) ══════════
        // Vấn đề: nhiều CardData có thể TRÙNG cardName (lá gốc vs levelUpForm, token...). Deck truyền
        // qua mạng chỉ là TÊN → nếu 2 máy resolve khác asset (khác thứ tự nguồn) → desync.
        // Fix: quét MỌI CardData, SORT theo tên asset (Object.name) ordinal → thứ tự giống nhau 2 máy;
        //      ƯU TIÊN lá DECK ĐƯỢC (không phải levelUpForm của lá khác, không isGenerated) →
        //      một tên trùng luôn ra LÁ GỐC trên cả 2 máy.
        Dictionary<string, CardData> _resolverCache;
        Dictionary<string, CardData> DeterministicResolver()
        {
            if (_resolverCache != null) return _resolverCache;

            var all = Resources.FindObjectsOfTypeAll<CardData>();
            var forms = new HashSet<CardData>();                 // lá là levelUpForm của lá khác → bỏ ưu tiên
            foreach (var c in all) if (c != null && c.levelUpForm != null) forms.Add(c.levelUpForm);

            var list = new List<CardData>();
            foreach (var c in all) if (c != null && !string.IsNullOrEmpty(c.cardName)) list.Add(c);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));   // deterministic 2 máy

            var map = new Dictionary<string, CardData>();
            int ambiguous = 0;
            // Pass 1: lá base deck-được (ưu tiên)
            foreach (var c in list)
                if (!c.isGenerated && !forms.Contains(c))
                {
                    if (map.ContainsKey(c.cardName)) ambiguous++;
                    else map[c.cardName] = c;
                }
            // Pass 2: điền nốt tên chưa có (form/generated) — để không thiếu lá
            foreach (var c in list)
                if (!map.ContainsKey(c.cardName)) map[c.cardName] = c;

            _resolverCache = map;
            Debug.Log($"[PvP] Resolver deterministic: {map.Count} tên (đã ưu tiên lá base). " +
                      (ambiguous > 0 ? $"⚠ {ambiguous} tên TRÙNG giữa nhiều lá base — nên đặt cardName duy nhất để tránh mơ hồ." : ""));
            return _resolverCache;
        }

        // Gộp fallbackDecks (field) + deck preset của GameController làm nguồn resolve dự phòng.
        DeckData[] ResolveFallbackDecks()
        {
            var list = new List<DeckData>();
            if (fallbackDecks != null)
                foreach (var d in fallbackDecks) if (d != null) list.Add(d);
            var gc = GameController.Instance;
            if (gc != null)
            {
                if (gc.playerDeckData != null) list.Add(gc.playerDeckData);
                if (gc.enemyDeckData != null) list.Add(gc.enemyDeckData);
            }
            return list.ToArray();
        }

        // Chẩn đoán: netId này đang ở đâu trên máy nhận? (phát hiện lệch state)
        static string WhereIs(GameModel m, int netId)
        {
            if (netId < 0) return "netId=-1 (lá tạo runtime CHƯA gán netId — v2)";
            foreach (var (name, p) in new[] { ("player", m.player), ("enemy", m.enemy) })
            {
                foreach (var c in p.hand) if (c.netId == netId) return $"{name}.HAND (ok đáng lẽ tìm thấy?)";
                foreach (var c in p.deck) if (c.netId == netId) return $"{name}.DECK ⇒ 2 MÁY LỆCH BÀI (mulligan/seed chưa đồng bộ)";
                foreach (var c in p.BenchCards()) if (c.netId == netId) return $"{name}.BENCH";
                foreach (var c in p.BattlefieldCards()) if (c.netId == netId) return $"{name}.BATTLEFIELD";
            }
            return "KHÔNG có ở đâu (netId không tồn tại trên máy này — lệch nặng)";
        }

        // ── RELAY ACTION ─────────────────────────────────────────────
        void SendAction(PlayerAction a)
        {
            var dto = ActionCodec.ToDTO(a);
            if (dto == null) return; // action v2 chưa hỗ trợ
            photonView.RPC(nameof(RpcAction), RpcTarget.Others, JsonUtility.ToJson(dto));
        }

        // Hàng chờ action nhận từ mạng — KHÔNG áp ngay bằng HandleAction vì HandleAction có các
        // guard Reject khi máy đang bận animation (_isSummoningSequence/_isResolvingCombat/...).
        // Máy chủ chạy skill (vd rút bài từ deck) trong coroutine có animation; nếu gói tới đúng
        // lúc máy kia đang bận → bị Reject → skill KHÔNG chạy bên đó → DESYNC.
        // Cách đúng (lockstep): xếp hàng theo THỨ TỰ, chờ máy RẢNH (IsBusy=false) rồi mới áp.
        readonly Queue<PlayerAction> _remoteActions = new Queue<PlayerAction>();
        Coroutine _remoteDrainCo;

        [PunRPC]
        void RpcAction(string json)   // chạy trên máy KIA (đối thủ)
        {
            var gc = GameController.Instance;
            var dto = JsonUtility.FromJson<ActionDTO>(json);
            var action = ActionCodec.ToAction(dto, gc.model); // byPlayer canonical nằm trong dto
            if (action == null)
            {
                Debug.LogWarning($"[Net] Không dựng được action '{dto.type}' netId={dto.cardId}. → {WhereIs(gc.model, dto.cardId)}");
                return;
            }
            _remoteActions.Enqueue(action);                       // giữ đúng thứ tự nhận
            if (_remoteDrainCo == null) _remoteDrainCo = StartCoroutine(DrainRemoteActions());
            // hash sẽ tự gửi qua debounce (host OnStateChanged) sau khi state lắng
        }

        // Áp lần lượt các action mạng: mỗi action CHỜ tới khi máy rảnh (không đang summon/combat/
        // spell/targeting/cinematic) rồi mới HandleAction → không bao giờ bị Reject vì đang bận.
        IEnumerator DrainRemoteActions()
        {
            var gc = GameController.Instance;
            while (_remoteActions.Count > 0)
            {
                // Chờ máy rảnh. Timeout dài (last-resort) chỉ để chống treo nếu 1 guard kẹt bất thường.
                float t0 = Time.realtimeSinceStartup;
                while (gc != null && gc.IsBusy)
                {
                    if (Time.realtimeSinceStartup - t0 > 30f)
                    { Debug.LogWarning("[Net] DrainRemoteActions chờ IsBusy quá lâu — áp ép để không treo."); break; }
                    yield return null;
                }
                if (gc == null) break;
                var a = _remoteActions.Dequeue();
                gc.HandleAction(a);   // KHÔNG SubmitLocal → tránh phát ngược
                yield return null;    // để coroutine của action này (nếu có) kịp set cờ IsBusy trước action kế
            }
            _remoteDrainCo = null;
        }

        // ── RELAY ĐẦU HÀNG ───────────────────────────────────────────
        void SendConcede(bool concederIsPlayerSide)
            => photonView.RPC(nameof(RpcConcede), RpcTarget.Others, concederIsPlayerSide);

        [PunRPC]
        void RpcConcede(bool concederIsPlayerSide)   // chạy trên máy KIA
            => GameController.Instance?.ApplyRemoteConcede(concederIsPlayerSide);

        // ── RELAY MULLIGAN ───────────────────────────────────────────
        void SendMulligan(List<CardModel> swap)
        {
            var ids = new List<int>();
            if (swap != null) foreach (var c in swap) if (c != null) ids.Add(c.netId);
            photonView.RPC(nameof(RpcMulligan), RpcTarget.Others, ids.ToArray());
        }

        [PunRPC]
        void RpcMulligan(int[] ids)   // chạy trên máy KIA
        {
            var gc = GameController.Instance;
            bool remoteByPlayer = !GameController.NetLocalIsPlayer;         // phe của đối thủ (canonical)
            var remoteHand = (remoteByPlayer ? gc.model.player : gc.model.enemy).hand;
            var cards = new List<CardModel>();
            foreach (var id in ids)
                foreach (var c in remoteHand) if (c.netId == id) { cards.Add(c); break; }
            gc.HandleMulliganConfirm(remoteByPlayer, cards); // mulligan của ĐỐI THỦ
        }

        // ── RELAY TARGET (spell — keyed theo netId lá spell, dùng cho spell tay có netId) ──
        void SendSpellTargets(CardModel source, List<CardModel> targets)
        {
            if (source == null) return;
            var ids = new List<int>();
            if (targets != null) foreach (var c in targets) if (c != null) ids.Add(c.netId);
            photonView.RPC(nameof(RpcSpellTargets), RpcTarget.Others, source.netId, ids.ToArray());
        }

        [PunRPC]
        void RpcSpellTargets(int sourceNetId, int[] targetIds)   // chạy trên máy KIA
        {
            var gc = GameController.Instance;
            var source = FindByNet(gc.model, sourceNetId);
            if (source == null) { Debug.LogWarning($"[Net] SpellTargets: không thấy source netId={sourceNetId}"); return; }
            var targets = new List<CardModel>();
            foreach (var id in targetIds)
            {
                var c = FindByNet(gc.model, id);
                if (c != null) targets.Add(c);
            }
            gc.SetSpellTargets(source, targets); // set target TRƯỚC khi PassPriority(commit) relay tới
        }

        // ── RELAY TARGET (UNIT-SKILL — keyed theo netId của UNIT + gen activation) ──
        // KHÔNG theo lá skill runtime (netId=-1). gen phân biệt các lần kích của cùng unit
        // → gói cũ đến muộn không bị activation sau nhặt nhầm.
        void SendUnitSkillTargets(CardModel unit, int gen, List<CardModel> targets)
        {
            if (unit == null) return;
            var ids = new List<int>();
            if (targets != null) foreach (var c in targets) if (c != null) ids.Add(c.netId);
            photonView.RPC(nameof(RpcUnitSkillTargets), RpcTarget.Others, unit.netId, gen, ids.ToArray());
        }

        [PunRPC]
        void RpcUnitSkillTargets(int unitNetId, int gen, int[] targetIds)   // chạy trên máy KIA (không phải chủ unit)
        {
            var gc = GameController.Instance;
            var targets = new List<CardModel>();
            foreach (var id in targetIds)
            {
                var c = FindByNet(gc.model, id);
                if (c != null) targets.Add(c);
            }
            // Lưu vào hàng chờ theo (netId unit, gen) → coroutine StageUnitSkillSpell/StageBattleSkillsCo lấy.
            gc.ReceiveUnitSkillTargets(unitNetId, gen, targets);
        }

        static CardModel FindByNet(GameModel m, int netId)
        {
            if (netId < 0) return null;
            foreach (var side in new[] { m.player, m.enemy })
            {
                foreach (var c in side.hand) if (c.netId == netId) return c;
                foreach (var c in side.BenchCards()) if (c.netId == netId) return c;
                foreach (var c in side.BattlefieldCards()) if (c.netId == netId) return c;
                foreach (var c in side.stagedSpells) if (c != null && c.netId == netId) return c;
            }
            return null;
        }

        // ── DESYNC DETECTOR ──────────────────────────────────────────
        Coroutine _hashCo;
        void ScheduleHash()
        {
            if (!LocalIsHost) return;
            if (_hashCo != null) StopCoroutine(_hashCo);
            _hashCo = StartCoroutine(DebouncedHash());
        }

        IEnumerator DebouncedHash()
        {
            // đợi state lắng (animation summon/combat ~<1s xong trên cả 2 máy) rồi mới so
            yield return new WaitForSecondsRealtime(1.2f);
            if (GameController.Instance?.model != null)
                photonView.RPC(nameof(RpcHash), RpcTarget.Others, GameController.Instance.StateFingerprint());
        }

        [PunRPC]
        void RpcHash(string hostFp)   // chạy trên CLIENT
        {
            if (GameController.Instance?.model == null) return;
            string mine = GameController.Instance.StateFingerprint();
            if (mine == hostFp) return;

            // GUARD: bỏ qua khi 2 máy đang ở KHÁC vòng (token "rN" đầu fingerprint) — lệch nhịp vòng
            // (buff đầu vòng / lá vừa rút chỉ mới áp 1 bên) → lệch GIẢ, tự khớp ở hash kế. Chỉ báo khi CÙNG vòng.
            if (RoundToken(hostFp) != RoundToken(mine)) return;

            Debug.LogError("[Net] ⚠ DESYNC! Lệch state (cùng vòng):");
            Debug.LogError($"   HOST  : {hostFp}");
            Debug.LogError($"   CLIENT: {mine}");
        }

        // Token vòng = phần trước dấu '|' đầu tiên của fingerprint (vd "r2").
        static string RoundToken(string fp)
        {
            if (string.IsNullOrEmpty(fp)) return "";
            int i = fp.IndexOf('|');
            return i < 0 ? fp : fp.Substring(0, i);
        }
    }
}