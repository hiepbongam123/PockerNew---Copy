using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// ─────────────────────────────────────────────────────────────────────────────
//  AudioSystem.cs — GỘP 4 module audio "auto, zero-setup" vào 1 file:
//    • ProceduralAudio  : sinh SFX bằng waveform runtime (cache theo key).
//    • ProceduralMusic  : sinh BGM loop bằng waveform runtime (calm/battle).
//    • BgmPlayer        : nhạc nền auto theo scene (DontDestroyOnLoad, code-boot).
//    • UiSfx/UiSfxDriver : click/hover SFX cho mọi UI (DontDestroyOnLoad, code-boot).
//
//  KHÔNG gộp AudioManager (nó gắn Inspector ở scene gameplay → phải giữ file riêng).
//  Cả 4 class ở đây đều tạo bằng AddComponent trong code → gộp chung 1 file an toàn.
//
//  XÓA sau khi thêm file này: ProceduralAudio.cs, ProceduralMusic.cs, BgmPlayer.cs, UiSfx.cs
// ─────────────────────────────────────────────────────────────────────────────
namespace LoRClone
{
    // ═════════════════════════════════════════════════════════════════════════
    //  SFX SYNTH
    // ═════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Sinh SFX bằng tổng hợp waveform tại runtime (AudioClip.Create) — KHÔNG cần file/asset.
    /// Dùng: AudioManager.Play(ProceduralAudio.Get("click"));  Clip cache theo key.
    /// Key: click, hover, confirm, clash, attack, hit, death, draw, heal, soft, mana, coin,
    ///   error, thud, shield, swift, shimmer, lowbrass, roundStart, endTurn, levelUp,
    ///   spellCast, victory, defeat.
    /// </summary>
    public static class ProceduralAudio
    {
        const int SR = 44100;
        static readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();

        public enum Wave { Sine, Square, Triangle, Saw, Noise }

        public static AudioClip Get(string key)
        {
            if (_cache.TryGetValue(key, out var c) && c != null) return c;
            c = Build(key);
            _cache[key] = c;
            return c;
        }

        static AudioClip Build(string key)
        {
            switch (key)
            {
                case "click":      return Clip(key, Osc(1100f, 1100f, 0.045f, Wave.Square, 0.30f, 0.001f));
                case "hover":      return Clip(key, Osc(760f,  820f,  0.05f,  Wave.Sine,   0.15f, 0.004f));
                case "confirm":    return Clip(key, Arp(new[] { 660f, 990f }, 0.05f, Wave.Square, 0.28f));
                case "clash":      return Clip(key, Mix(Osc(0,0,0.16f, Wave.Noise, 0.35f, 0.001f),
                                                        Osc(340f, 110f, 0.16f, Wave.Saw, 0.35f, 0.001f)));
                case "attack":     return Clip(key, Mix(Osc(0,0,0.10f, Wave.Noise, 0.25f, 0.001f),
                                                        Osc(420f, 180f, 0.10f, Wave.Square, 0.30f, 0.001f)));
                case "hit":        return Clip(key, Mix(Osc(0,0,0.09f, Wave.Noise, 0.30f, 0.001f),
                                                        Osc(180f, 90f,  0.09f, Wave.Square, 0.35f, 0.001f)));
                case "death":      return Clip(key, Osc(300f, 60f, 0.40f, Wave.Saw, 0.30f, 0.003f));
                case "draw":       return Clip(key, Osc(420f, 900f, 0.13f, Wave.Triangle, 0.22f, 0.003f));
                case "heal":       return Clip(key, Arp(new[] { 660f, 880f, 1180f }, 0.06f, Wave.Sine, 0.22f));
                case "soft":       return Clip(key, Osc(520f, 560f, 0.08f, Wave.Sine, 0.14f, 0.006f));
                case "mana":       return Clip(key, Mix(Osc(920f, 920f, 0.28f, Wave.Sine, 0.22f, 0.006f),
                                                        Osc(1380f,1380f,0.28f, Wave.Sine, 0.11f, 0.006f)));
                case "coin":       return Clip(key, Arp(new[] { 1568f, 2093f }, 0.06f, Wave.Square, 0.22f));
                case "error":      return Clip(key, Mix(Osc(150f, 150f, 0.22f, Wave.Square, 0.28f, 0.002f),
                                                        Osc(158f, 158f, 0.22f, Wave.Square, 0.20f, 0.002f)));
                case "thud":       return Clip(key, Osc(120f, 70f, 0.14f, Wave.Sine, 0.35f, 0.002f));
                case "shield":     return Clip(key, Mix(Osc(600f, 700f, 0.20f, Wave.Sine, 0.20f, 0.005f),
                                                        Osc(900f, 900f, 0.20f, Wave.Triangle, 0.10f, 0.005f)));
                case "swift":      return Clip(key, Osc(1300f, 500f, 0.10f, Wave.Sine, 0.20f, 0.003f));
                case "shimmer":    return Clip(key, Osc(700f, 1900f, 0.22f, Wave.Sine, 0.16f, 0.006f));
                case "lowbrass":   return Clip(key, Osc(110f, 90f, 0.35f, Wave.Saw, 0.28f, 0.004f));
                case "roundStart": return Clip(key, Arp(new[] { 523f, 784f }, 0.12f, Wave.Sine, 0.24f));
                case "endTurn":    return Clip(key, Osc(300f, 220f, 0.10f, Wave.Sine, 0.22f, 0.004f));
                case "levelUp":    return Clip(key, Arp(new[] { 523f, 659f, 784f, 1047f }, 0.08f, Wave.Square, 0.24f));
                case "spellCast":  return Clip(key, Mix(Osc(500f, 1500f, 0.26f, Wave.Sine, 0.20f, 0.005f),
                                                        Osc(1000f,2000f, 0.26f, Wave.Triangle, 0.08f, 0.005f)));
                case "victory":    return Clip(key, Arp(new[] { 523f, 659f, 784f, 1047f, 1319f }, 0.11f, Wave.Square, 0.26f));
                case "defeat":     return Clip(key, Arp(new[] { 523f, 440f, 349f, 262f }, 0.14f, Wave.Saw, 0.24f));
                default:           return Clip("beep", Osc(660f, 660f, 0.08f, Wave.Sine, 0.2f, 0.004f));
            }
        }

        static float[] Osc(float f0, float f1, float dur, Wave w, float vol, float atk)
        {
            int n = Mathf.Max(1, (int)(SR * dur));
            var buf = new float[n];
            double phase = 0;
            float tail = Mathf.Max(1e-4f, dur - atk);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float f = Mathf.Lerp(f0, f1, dur > 0f ? t / dur : 0f);
                phase += 2.0 * Mathf.PI * f / SR;
                float s = SampleWave(w, (float)phase);
                float a = atk > 0f ? Mathf.Min(1f, t / atk) : 1f;
                float x = Mathf.Max(0f, t - atk);
                float d = Mathf.Exp(-4f * x / tail);
                buf[i] = s * a * d * vol;
            }
            return buf;
        }

        static float SampleWave(Wave w, float phase)
        {
            switch (w)
            {
                case Wave.Sine:     return Mathf.Sin(phase);
                case Wave.Square:   return Mathf.Sin(phase) >= 0f ? 1f : -1f;
                case Wave.Triangle: return (2f / Mathf.PI) * Mathf.Asin(Mathf.Sin(phase));
                case Wave.Saw:      { float p = Mathf.Repeat(phase / (2f * Mathf.PI), 1f); return 2f * p - 1f; }
                case Wave.Noise:    return Random.Range(-1f, 1f);
                default:            return 0f;
            }
        }

        static float[] Arp(float[] freqs, float noteDur, Wave w, float vol)
        {
            var parts = new List<float[]>(freqs.Length);
            foreach (var f in freqs) parts.Add(Osc(f, f, noteDur, w, vol, 0.002f));
            return Concat(parts);
        }

        static float[] Concat(List<float[]> parts)
        {
            int total = 0; foreach (var p in parts) total += p.Length;
            var buf = new float[total]; int o = 0;
            foreach (var p in parts) { System.Array.Copy(p, 0, buf, o, p.Length); o += p.Length; }
            return buf;
        }

        static float[] Mix(float[] a, float[] b)
        {
            int n = Mathf.Max(a.Length, b.Length);
            var buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = 0f;
                if (i < a.Length) s += a[i];
                if (i < b.Length) s += b[i];
                buf[i] = Mathf.Clamp(s, -1f, 1f);
            }
            return buf;
        }

        static AudioClip Clip(string name, float[] data)
        {
            var clip = AudioClip.Create("pa_" + name, data.Length, 1, SR, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  BGM SYNTH
    // ═════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Sinh nhạc nền (BGM) loop bằng tổng hợp waveform tại runtime — KHÔNG cần file nhạc.
    /// Style: "calm" (menu/lobby) | "battle" (gameplay). Dùng: ProceduralMusic.Get("calm");
    /// </summary>
    public static class ProceduralMusic
    {
        const int SR = 44100;
        static readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();

        enum W { Sine, Triangle, Saw }

        public static AudioClip Get(string style)
        {
            if (_cache.TryGetValue(style, out var c) && c != null) return c;
            c = Build(style);
            _cache[style] = c;
            return c;
        }

        static AudioClip Build(string style)
        {
            int[][] chords; int[] bass; int bpm;
            if (style == "battle")
            {
                chords = new[] { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 55, 59, 62 }, new[] { 52, 56, 59 } };
                bass   = new[] { 45, 41, 43, 40 };
                bpm = 100;
            }
            else
            {
                chords = new[] { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 60, 64, 67 }, new[] { 55, 59, 62 } };
                bass   = new[] { 45, 41, 48, 43 };
                bpm = 72;
            }

            float beat = 60f / bpm;
            float bar = 4f * beat;
            int bars = chords.Length;
            int n = Mathf.CeilToInt(bar * bars * SR);
            var buf = new float[n];

            bool driving = style == "battle";
            float arpVol = driving ? 0.16f : 0.12f;
            W arpWave = driving ? W.Saw : W.Triangle;

            for (int b = 0; b < bars; b++)
            {
                int barStart = (int)(b * bar * SR);
                int[] ch = chords[b];

                foreach (int m in ch)
                    Add(buf, barStart, Midi(m), bar, W.Sine, 0.10f, 0f, cosine: true);

                Add(buf, barStart,                         Midi(bass[b]), 2f * beat, W.Sine, 0.22f, 0.005f, false);
                Add(buf, barStart + (int)(2f * beat * SR), Midi(bass[b]), 2f * beat, W.Sine, 0.22f, 0.005f, false);

                for (int e = 0; e < 8; e++)
                {
                    int m = ch[e % ch.Length] + 12;
                    int s = barStart + (int)(e * 0.5f * beat * SR);
                    Add(buf, s, Midi(m), 0.45f * beat, arpWave, arpVol, 0.004f, false);
                }
            }

            float peak = 0f;
            for (int i = 0; i < n; i++) { float a = Mathf.Abs(buf[i]); if (a > peak) peak = a; }
            if (peak > 0.0001f)
            {
                float g = 0.8f / peak;
                for (int i = 0; i < n; i++) buf[i] *= g;
            }

            int fade = (int)(0.005f * SR);
            for (int i = 0; i < fade && i < n; i++)
            {
                float k = (float)i / fade;
                buf[i] *= k;
                buf[n - 1 - i] *= k;
            }

            var clip = AudioClip.Create("bgm_" + style, n, 1, SR, false);
            clip.SetData(buf, 0);
            return clip;
        }

        static void Add(float[] buf, int start, float freq, float dur, W w, float vol, float atk, bool cosine)
        {
            int n = (int)(SR * dur);
            double ph = 0;
            float tail = Mathf.Max(1e-4f, dur - atk);
            for (int i = 0; i < n; i++)
            {
                int idx = start + i;
                if (idx < 0) continue;
                if (idx >= buf.Length) break;
                float t = (float)i / SR;
                ph += 2.0 * Mathf.PI * freq / SR;
                float s = Sample(w, ph);
                float env;
                if (cosine)
                    env = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * (t / dur)));
                else
                {
                    float a = atk > 0f ? Mathf.Min(1f, t / atk) : 1f;
                    float d = Mathf.Exp(-3.5f * Mathf.Max(0f, t - atk) / tail);
                    env = a * d;
                }
                buf[idx] += s * env * vol;
            }
        }

        static float Sample(W w, double ph)
        {
            switch (w)
            {
                case W.Sine:     return Mathf.Sin((float)ph);
                case W.Triangle: return (2f / Mathf.PI) * Mathf.Asin(Mathf.Sin((float)ph));
                case W.Saw:      { float p = Mathf.Repeat((float)(ph / (2.0 * Mathf.PI)), 1f); return 2f * p - 1f; }
                default:         return 0f;
            }
        }

        static float Midi(int m) => 440f * Mathf.Pow(2f, (m - 69) / 12f);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  BGM PLAYER (auto theo scene)
    // ═════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Nhạc nền cho MỌI scene — KHÔNG cần gán track, KHÔNG cần AudioManager.
    /// Đổi nhạc theo tên scene (menu → "calm", gameplay → "battle"), crossfade khi đổi scene.
    /// FIX: nhường AudioManager cho MỌI scene (kể cả lobby) khi scene có track thật / đang phát nhạc thật.
    /// </summary>
    public class BgmPlayer : MonoBehaviour
    {
        public static BgmPlayer Instance { get; private set; }

        static float _volume = 0.35f;
        static bool _muted = false;

        static readonly string[] MenuHints = { "lobby", "menu", "title", "home", "start" };
        static readonly string[] GameHints = { "game", "battle", "play", "match", "combat", "board" };

        AudioSource _a, _b;
        bool _useA = true;
        string _current = "";
        Coroutine _fadeCo;
        const float FadeDur = 1.2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("~Bgm");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<BgmPlayer>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _a = gameObject.AddComponent<AudioSource>(); Cfg(_a);
            _b = gameObject.AddComponent<AudioSource>(); Cfg(_b);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void Start() => Apply(SceneManager.GetActiveScene().name);

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        static void Cfg(AudioSource s)
        {
            s.playOnAwake = false; s.loop = true; s.spatialBlend = 0f; s.volume = 0f;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode m) => Apply(s.name);

        void Apply(string sceneName)
        {
            string style = StyleFor(sceneName);

            // ★ FIX: nhường AudioManager cho MỌI scene (không chỉ battle) nếu có nhạc thật.
            if (AudioManager.Instance != null
                && (AudioManager.Instance.HasGameplayMusic || AudioManager.MusicActive))
            { FadeToSilence(); _current = "defer"; return; }

            if (_current == style) return;
            var clip = ProceduralMusic.Get(style);
            if (clip == null) return;
            CrossfadeTo(clip, style);
        }

        static string StyleFor(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            foreach (var g in GameHints) if (n.Contains(g)) return "battle";
            foreach (var mn in MenuHints) if (n.Contains(mn)) return "calm";
            return "calm";
        }

        void CrossfadeTo(AudioClip clip, string key)
        {
            var to = _useA ? _b : _a;
            var from = _useA ? _a : _b;
            _useA = !_useA;
            to.clip = clip; to.volume = 0f; to.Play();
            _current = key;
            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(FadeCo(from, to));
        }

        IEnumerator FadeCo(AudioSource from, AudioSource to)
        {
            float f0 = from != null ? from.volume : 0f;
            float target = _muted ? 0f : _volume;
            for (float t = 0; t < FadeDur; t += Time.unscaledDeltaTime)
            {
                float k = t / FadeDur;
                if (to != null) to.volume = k * target;
                if (from != null) from.volume = (1f - k) * f0;
                yield return null;
            }
            if (to != null) to.volume = target;
            if (from != null) { from.volume = 0f; from.Stop(); }
            _fadeCo = null;
        }

        void FadeToSilence()
        {
            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(FadeCo(_useA ? _b : _a, null));
        }

        AudioSource ActivePlaying => _a != null && _a.isPlaying ? _a : _b;

        public static void SetVolume(float v)
        {
            _volume = Mathf.Clamp01(v);
            if (Instance != null && !_muted)
            {
                var s = Instance.ActivePlaying;
                if (s != null && Instance._fadeCo == null) s.volume = _volume;
            }
        }

        public static void Mute(bool m)
        {
            _muted = m;
            if (Instance != null)
            {
                var s = Instance.ActivePlaying;
                if (s != null && Instance._fadeCo == null) s.volume = m ? 0f : _volume;
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  UI SFX (click/hover auto)
    // ═════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// UI click/hover SFX cho MỌI scene — KHÔNG cần AudioManager, KHÔNG cần gán listener.
    /// Tự tạo GameObject ẩn (DontDestroyOnLoad) + AudioSource, raycast UI mỗi frame.
    /// </summary>
    public static class UiSfx
    {
        public static float volume = 0.6f;
        public static bool enabled = true;

        static AudioSource _src;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("~UiSfx");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            go.AddComponent<UiSfxDriver>();
        }

        public static void Play(string key, float vol = 1f)
        {
            if (!enabled || _src == null) return;
            var c = ProceduralAudio.Get(key);
            if (c != null) _src.PlayOneShot(c, Mathf.Clamp01(volume * vol));
        }

        public static void Click() => Play("click");
        public static void Hover() => Play("hover", 0.7f);
    }

    /// <summary>Driver ẩn: raycast UI mỗi frame để phát click/hover. Tự gắn bởi UiSfx.Boot().</summary>
    public class UiSfxDriver : MonoBehaviour
    {
        Button _hover;
        static readonly List<RaycastResult> _hits = new List<RaycastResult>();

        void Update()
        {
            var es = EventSystem.current;
            if (es == null) return;

            Button b = ButtonUnder(es, Input.mousePosition);

            if (Input.GetMouseButtonDown(0) && b != null && b.interactable)
                UiSfx.Click();

            if (b != _hover)
            {
                if (b != null && b.interactable) UiSfx.Hover();
                _hover = b;
            }
        }

        static Button ButtonUnder(EventSystem es, Vector2 pos)
        {
            var ped = new PointerEventData(es) { position = pos };
            _hits.Clear();
            es.RaycastAll(ped, _hits);
            for (int i = 0; i < _hits.Count; i++)
            {
                var btn = _hits[i].gameObject.GetComponentInParent<Button>();
                if (btn != null) return btn;
            }
            return null;
        }
    }
}
