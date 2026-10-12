using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.EnvironmentEffect;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace OBMTheCompound.Client
{
    // Client side of The Compound (SPT location Terminal). The map uses the Woods scripts scene
    // (sky, weather, time of day); we replace its indoor areas and rain roofs with our own.
    [BepInPlugin("private.obm.thecompound.client", "OBM The Compound (Client)", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        void Awake()
        {
            Log = Logger;
            new GameStartedPatch().Enable();
            BotActivateDiag.Apply(Logger);
            try { CompoundDestruction.Patch(new Harmony("private.obm.thecompound.destruction")); }
            catch (Exception e) { Logger.LogError("The Compound: Zerstoerung nicht angeschlossen: " + e); }
        }

        // run after all plugins are loaded, ABPS can load after us
        void Start() => AbpsCompat.Apply(Logger);
    }

    public class GameStartedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));

        [PatchPostfix]
        static void Postfix(GameWorld __instance)
        {
            if (!string.Equals(__instance.LocationId, "Terminal", StringComparison.OrdinalIgnoreCase)) return;
            try { CompoundEnvironment.Apply(); }
            catch (Exception e) { Plugin.Log.LogError("The Compound: Umgebung konnte nicht uebernommen werden: " + e); }
            try { CompoundDestruction.Collect(); }
            catch (Exception e) { Plugin.Log.LogError("The Compound: Zerstoerbare Waende nicht erfasst: " + e); }
        }
    }

    public static class CompoundEnvironment
    {
        public static void Apply()
        {
            var root = GameObject.Find("OBM_Environment");
            if (root == null) { Plugin.Log.LogWarning("The Compound: OBM_Environment nicht gefunden (Map neu bauen?)"); return; }

            // indoor areas: use our TriggerGroup as the root group
            var group = root.transform.Find("OBM_IndoorTriggers")?.GetComponent<TriggerGroup>();
            var env = EnvironmentManagerBase.Instance;
            if (group != null && env != null)
            {
                group.Reinit();
                AccessTools.Field(typeof(EnvironmentManagerBase), "_rootTriggerGroup").SetValue(env, group);
                (AccessTools.Field(typeof(EnvironmentManagerBase), "_cachedPlayerTrigger")?.GetValue(env) as IDictionary)?.Clear();
                foreach (var f in AccessTools.GetDeclaredFields(typeof(EnvironmentManagerBase)).Where(f => f.FieldType == typeof(bool) && f.Name == "bool_0"))
                    f.SetValue(env, true); // has trigger group
                Plugin.Log.LogInfo($"The Compound: {group._childrenTriggers.Count} Innenraeume aktiv");
            }

            // rain roofs: swap in our WeatherObstacle and re-render the rain depth map
            var roofs = root.transform.Find("OBM_DryPlanes");
            if (roofs != null)
            {
                var woods = WeatherObstacle.Instance;
                roofs.gameObject.SetActive(true); // Awake merges the DryPlanes into the roof mesh
                var ours = roofs.GetComponent<WeatherObstacle>();
                AccessTools.Field(typeof(WeatherObstacle), "weatherObstacle_0").SetValue(null, ours);
                if (woods != null && woods != ours) woods.gameObject.SetActive(false);

                var levelSettings = Singleton<LevelSettings>.Instance;
                if (levelSettings != null) levelSettings.RainBounds = MapBounds();
                DepthPhotograper.Instance?.Render();
                Plugin.Log.LogInfo($"The Compound: Regendach mit {roofs.childCount} Flaechen aktiv");
            }

            // lamps and ambient sounds from Unity
            new GameObject("OBM_LightsAndSounds").AddComponent<CompoundLightsAndSounds>();
            new GameObject("OBM_ShaderLab").AddComponent<CompoundShaderLab>();
            new GameObject("OBM_BotDiag").AddComponent<CompoundBotDiag>();
            new GameObject("OBM_BotRespawn").AddComponent<CompoundBotRespawn>();
        }

        // terrains and geometry of our zone scenes
        static Bounds MapBounds()
        {
            var b = new Bounds(Vector3.zero, Vector3.zero);
            var first = true;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Terrain>().Where(t => t.gameObject.scene.name.StartsWith("OBM_TC")))
            {
                var tb = new Bounds(t.transform.position + t.terrainData.size * 0.5f, t.terrainData.size);
                if (first) { b = tb; first = false; } else b.Encapsulate(tb);
            }
            if (first) b = new Bounds(Vector3.zero, new Vector3(1200, 220, 1200));
            b.Expand(new Vector3(20, 200, 20));
            return b;
        }
    }

    // Lamps are plain Unity lights, turned off beyond range + 60 m (checked twice per second).
    // All our AudioSources go through Tarkov's environment mixer so the volume slider works.
    // OBM_AMB_RANDOM_<min>_<max> objects play a random clip every min..max/10 seconds.
    public class CompoundLightsAndSounds : MonoBehaviour
    {
        const float Margin = 60f;
        Light[] _lights;
        float[] _cull;
        EFT.Interactive.LampController[] _lamps;
        (Transform t, AudioSource[] clips, float min, float max, float next)[] _random;

        void Start()
        {
            // objects without a scene (prefabs, DontDestroyOnLoad)
            bool Ours(Component c) => c != null && c.gameObject.scene.IsValid() && (c.gameObject.scene.name ?? "").StartsWith("OBM_TC");
            _lights = FindObjectsOfType<Light>(true).Where(l => Ours(l) && l.type != LightType.Directional && l.enabled).ToArray();
            _cull = _lights.Select(l => l.range + Margin).ToArray();
            _lamps = _lights.Select(l => l.GetComponentInParent<EFT.Interactive.LampController>()).ToArray();

            var audio = Singleton<BetterAudio>.Instance;
            var mixer = audio != null ? (audio.OutEnvironment ?? audio.VeryStandartMixerGroup) : null;
            var sources = FindObjectsOfType<AudioSource>(true).Where(Ours).ToArray();
            if (mixer != null)
                foreach (var s in sources) s.outputAudioMixerGroup = mixer;
            foreach (var s in sources.Where(s => s.loop && s.playOnAwake && s.isActiveAndEnabled && !s.isPlaying)) s.Play();

            _random = FindObjectsOfType<Transform>(true).Where(t => Ours(t) && t.name.StartsWith("OBM_AMB_RANDOM_")).Select(t =>
            {
                var parts = t.name.Split('_');
                float.TryParse(parts[3], out var min); float.TryParse(parts[4], out var max);
                min /= 10f; max = Mathf.Max(min, max / 10f);
                return (t, t.GetComponents<AudioSource>(), min, max, Time.time + UnityEngine.Random.Range(0f, max));
            }).Where(r => r.Item2.Length > 0).ToArray();
            Plugin.Log.LogInfo($"The Compound: {_lights.Length} Lampen, {sources.Length} Sounds ({_random.Length} zufaellig)");
            Invoke(nameof(LightmapDiagnose), 5f);
            InvokeRepeating(nameof(CullLights), 0.5f, 0.5f);
            InvokeRepeating(nameof(UpdateAmbient), 0f, 0.5f);
        }

        // baked lighting follows Tarkov's ambient light, passed to our map shader as _OBMAmbientTint (w = 1 enabled)
        static readonly int AmbientTint = Shader.PropertyToID("_OBMAmbientTint");
        readonly Vector3[] _up = { Vector3.up };
        readonly Color[] _probe = new Color[1];

        // Tarkov doesn't use RenderSettings ambient in raid, read the sky ambient instead
        static object _tod;
        static System.Reflection.PropertyInfo _todAmbient;
        static string _ambientSource = "?";

        static bool TodAmbient(out Color c)
        {
            c = Color.black;
            try
            {
                if (_tod == null || (_tod is UnityEngine.Object o && o == null))
                {
                    var t = AccessTools.TypeByName("TOD_Sky");
                    _tod = t == null ? null : AccessTools.Property(t, "Instance")?.GetValue(null, null);
                    _todAmbient = t == null ? null : AccessTools.Property(t, "AmbientColor");
                }
                if (_tod == null || _todAmbient == null) return false;
                c = (Color)_todAmbient.GetValue(_tod, null);
                return true;
            }
            catch { return false; }
        }

        public static bool TintDisabled;

        void UpdateAmbient()
        {
            if (TintDisabled) { Shader.SetGlobalVector(AmbientTint, Vector4.zero); return; }
            Color c;
            if (TodAmbient(out c)) _ambientSource = "TOD_Sky";
            else
            {
                switch (RenderSettings.ambientMode)
                {
                    case UnityEngine.Rendering.AmbientMode.Flat: c = RenderSettings.ambientLight; break;
                    case UnityEngine.Rendering.AmbientMode.Trilight: c = RenderSettings.ambientSkyColor; break;
                    default: RenderSettings.ambientProbe.Evaluate(_up, _probe); c = _probe[0]; break;
                }
                c *= RenderSettings.ambientIntensity > 0f ? RenderSettings.ambientIntensity : 1f;
                _ambientSource = "RenderSettings";
            }
            // never fully black, that would remove all baked light
            var lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            if (lum < 0.02f) c = lum > 0.0001f ? c * (0.02f / lum) : new Color(0.02f, 0.02f, 0.025f);
            Shader.SetGlobalVector(AmbientTint, new Vector4(Mathf.Clamp(c.r, 0f, 3f), Mathf.Clamp(c.g, 0f, 3f), Mathf.Clamp(c.b, 0f, 3f), 1f));
        }

        void OnDestroy() => Shader.SetGlobalVector(AmbientTint, Vector4.zero);

        // log which baked light made it into the game
        void LightmapDiagnose()
        {
            var maps = LightmapSettings.lightmaps;
            int total = 0, mapped = 0, bad = 0;
            foreach (var r in FindObjectsOfType<MeshRenderer>())
            {
                if (!r.gameObject.scene.IsValid() || !(r.gameObject.scene.name ?? "").StartsWith("OBM_TC_Zone")) continue;
                total++;
                if (r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE) { if (r.lightmapIndex < maps.Length && maps[r.lightmapIndex].lightmapColor != null) mapped++; else bad++; }
            }
            var names = string.Join(", ", maps.Take(12).Select(m => m.lightmapColor != null ? m.lightmapColor.name : "null"));
            var tint = Shader.GetGlobalVector(AmbientTint);
            Plugin.Log.LogInfo($"The Compound Licht: {maps.Length} Lightmaps geladen [{names}]; unsere Renderer {total}, mit gueltiger Lightmap {mapped}, mit ungueltigem Index {bad}; " +
                               $"Ambient-Tint {tint} (Quelle {_ambientSource}); AmbientMode {RenderSettings.ambientMode}, Intensitaet {RenderSettings.ambientIntensity}; LightmapsMode {LightmapSettings.lightmapsMode}");
        }

        void CullLights()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var p = cam.transform.position;
            for (var i = 0; i < _lights.Length; i++)
            {
                var l = _lights[i];
                if (l == null) continue;
                // keep shot out / switched off lamps dark
                var lamp = _lamps[i];
                if (lamp != null && lamp.LampState != EFT.Interactive.Turnable.EState.On) continue;
                var on = (l.transform.position - p).sqrMagnitude < _cull[i] * _cull[i];
                if (l.enabled != on) l.enabled = on;
            }
        }

        void Update()
        {
            var now = Time.time;
            if (_random == null) return;
            for (var i = 0; i < _random.Length; i++)
            {
                if (now < _random[i].next || _random[i].t == null) continue;
                var clips = _random[i].clips;
                var s = clips[UnityEngine.Random.Range(0, clips.Length)];
                if (s != null && s.clip != null) s.PlayOneShot(s.clip);
                _random[i].next = now + UnityEngine.Random.Range(_random[i].min, _random[i].max) + (s != null && s.clip != null ? s.clip.length : 0f);
            }
        }
    }

    // ABPS only supports vanilla maps (bot limit 0 elsewhere). On The Compound it runs with its Streets settings:
    // the location lookup returns tarkovstreets for Terminal and the bot limit is the Streets one.
    // The server mod copies the Streets spawns onto our zones. No-op without ABPS.
    public static class AbpsCompat
    {
        static Type _plugin;

        public static void Apply(ManualLogSource log)
        {
            var utility = AccessTools.TypeByName("BotPlacementSystemClient.Utils.Utility");
            _plugin = AccessTools.TypeByName("BotPlacementSystemClient.Plugin");
            if (utility == null || _plugin == null) return;
            var harmony = new Harmony("private.obm.thecompound.client.abps");
            harmony.Patch(AccessTools.PropertyGetter(utility, "CurrentLocation"),
                postfix: new HarmonyMethod(typeof(AbpsCompat), nameof(LocationPostfix)));
            harmony.Patch(AccessTools.Method(typeof(BotsController), "SetSettings"),
                postfix: new HarmonyMethod(typeof(AbpsCompat), nameof(MaxBotsPostfix)) { priority = Priority.Last });
            log.LogInfo("The Compound: ABPS erkannt – The Compound nutzt die ABPS-Einstellungen von Streets.");
        }

        static System.Reflection.MethodInfo _pmcTarget, _pmcPrefix;
        static string _pmcOwner;

        static void SetAbpsPmcHook(bool active)
        {
            try
            {
                _pmcTarget = _pmcTarget ?? AccessTools.Method(typeof(BossSpawnerClass), "method_2");
                if (_pmcTarget == null) return;
                if (!active)
                {
                    if (_pmcPrefix != null) return;
                    var p = Harmony.GetPatchInfo(_pmcTarget)?.Prefixes.FirstOrDefault(x => (x.PatchMethod.DeclaringType?.FullName ?? "").StartsWith("BotPlacementSystemClient"));
                    if (p == null) return;
                    _pmcPrefix = p.PatchMethod; _pmcOwner = p.owner;
                    new Harmony(_pmcOwner).Unpatch(_pmcTarget, _pmcPrefix);
                    Plugin.Log.LogInfo("The Compound: ABPS-PMC-Spawn auf The Compound aus (PMCs aus den eigenen Bot-Zonen)");
                }
                else if (_pmcPrefix != null)
                {
                    new Harmony(_pmcOwner).Patch(_pmcTarget, prefix: new HarmonyMethod(_pmcPrefix));
                    _pmcPrefix = null;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("The Compound: ABPS-PMC-Spawn konnte nicht umgeschaltet werden: " + e.Message); }
        }

        // same config.json as the server mod
        static bool? _streets;
        static bool StreetsMode()
        {
            if (_streets.HasValue) return _streets.Value;
            _streets = false;
            try
            {
                var cfg = System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "SPT", "user", "mods", "OBMTheCompound", "config.json");
                if (System.IO.File.Exists(cfg))
                    _streets = System.Text.RegularExpressions.Regex.IsMatch(System.IO.File.ReadAllText(cfg), "\"abpsStreetsSpawns\"\\s*:\\s*true");
            }
            catch (Exception) { }
            return _streets.Value;
        }

        // abpsPmcSpawns: true makes ABPS spawn PMCs at the map's PMC spawn points, needs a few spread out ones
        static bool? _abpsPmcs;
        static bool AbpsPmcs()
        {
            if (_abpsPmcs.HasValue) return _abpsPmcs.Value;
            _abpsPmcs = false;
            try
            {
                var cfg = System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "SPT", "user", "mods", "OBMTheCompound", "config.json");
                if (System.IO.File.Exists(cfg))
                    _abpsPmcs = System.Text.RegularExpressions.Regex.IsMatch(System.IO.File.ReadAllText(cfg), "\"abpsPmcSpawns\"\\s*:\\s*true");
            }
            catch (Exception) { }
            return _abpsPmcs.Value;
        }

        static bool OnCompound() => Singleton<GameWorld>.Instance is GameWorld gw && string.Equals(gw.LocationId, "Terminal", StringComparison.OrdinalIgnoreCase);

        static void LocationPostfix(ref string __result)
        {
            if (string.Equals(__result, "terminal", StringComparison.OrdinalIgnoreCase)) __result = "tarkovstreets";
        }

        // runs after ABPS' own SetSettings postfix, which sets 0 for unknown maps.
        // Default: keep the limit SPT passed in (sum of our bot zones).
        // With abpsStreetsSpawns: true use ABPS' Streets limit.
        static void MaxBotsPostfix(BotsController __instance, int maxCount)
        {
            if (!OnCompound()) { SetAbpsPmcHook(true); return; }
            // with our own bot zones PMCs spawn the normal way. ABPS' PMC hook looks for player spawns far
            // away from everyone, finds none on a small map and drops the PMC
            SetAbpsPmcHook(StreetsMode() || AbpsPmcs());
            var limit = StreetsMode() ? AccessTools.Field(_plugin, "StreetsMapLimit")?.GetValue(null) as int? ?? 0 : maxCount;
            if (limit <= 0) return;
            __instance.MaxCount = limit;
            if (__instance.BotSpawner != null)
            {
                __instance.BotSpawner.SetMaxBots(limit);
                __instance.ZonesLeaveController.SetMaxBots(limit);
            }
            Plugin.Log.LogInfo($"The Compound: Bot-Limit {limit} ({(StreetsMode() ? "ABPS Streets" : "eigene Bot-Zonen")})");
        }
    }
}


namespace OBMTheCompound.Client
{
    // Lighting debug keys (raid only). Our materials use Unity Standard, Tarkov maps use its p0 shaders.
    // Hotkeys to compare both live:
    //   Ctrl+Alt+1  our materials / Tarkov shaders
    //   Ctrl+Alt+2  baked lightmaps on/off
    //   Ctrl+Alt+3  lightmap tint on/off
    public class CompoundShaderLab : MonoBehaviour
    {
        static readonly string[] Ours = { "Standard", "Standard (Specular setup)", "OBM/Standard Specular (Lightmap)", "OBM/Standard Specular Cutout (Lightmap)" };
        readonly System.Collections.Generic.Dictionary<(Material, bool), Material> _cache = new System.Collections.Generic.Dictionary<(Material, bool), Material>();
        readonly System.Collections.Generic.List<(Renderer r, Material[] orig, int lightmap)> _renderers = new System.Collections.Generic.List<(Renderer, Material[], int)>();
        Shader _p0, _p0Baked, _p0Cutout;
        bool _tarkov, _noLightmaps, _noTint;
        // indoor darkening, Ctrl+Alt+4 cycles
        static readonly float[] DarkSteps = { 0.6f, 0.8f, 0.95f, 0f, 0.3f };
        // Tarkov indoor ambient (StencilShadow alpha), Ctrl+Alt+5 cycles
        static readonly float[] StencilSteps = { 0.834f, 0.92f, 0.98f, 0f, 0.6f };
        int _stencilIndex;
        static readonly int IndoorDarken = Shader.PropertyToID("_OBMIndoorDarken");
        int _darkIndex;

        void Start()
        {
            Shader.SetGlobalVector(IndoorDarken, new Vector4(DarkSteps[0], 0, 0, 1));
            _p0 = Shader.Find("p0/Reflective/Bumped Specular SMap");
            _p0Baked = Shader.Find("p0/Reflective/Bumped Specular SMap Baked");
            _p0Cutout = Shader.Find("p0/Cutout/Bumped Diffuse");
            var shaders = new System.Collections.Generic.Dictionary<string, int>();
            var layers = new System.Collections.Generic.Dictionary<int, int>();
            var casts = new System.Collections.Generic.Dictionary<UnityEngine.Rendering.ShadowCastingMode, int>();
            foreach (var r in FindObjectsOfType<MeshRenderer>())
            {
                var scene = r.gameObject.scene.name ?? "";
                if (!scene.StartsWith("OBM_TC")) continue;
                _renderers.Add((r, r.sharedMaterials, r.lightmapIndex));
                layers[r.gameObject.layer] = layers.TryGetValue(r.gameObject.layer, out var l) ? l + 1 : 1;
                casts[r.shadowCastingMode] = casts.TryGetValue(r.shadowCastingMode, out var c) ? c + 1 : 1;
                foreach (var m in r.sharedMaterials)
                {
                    var n = m != null && m.shader != null ? m.shader.name : "null";
                    shaders[n] = shaders.TryGetValue(n, out var k) ? k + 1 : 1;
                }
            }
            Plugin.Log.LogInfo($"The Compound Shader-Test: Tarkov-Shader gefunden: SMap={_p0 != null}, Baked={_p0Baked != null}, Cutout={_p0Cutout != null}; " +
                               $"unsere Renderer {_renderers.Count}; Shader: {string.Join(", ", shaders.OrderByDescending(x => x.Value).Take(8).Select(x => x.Key + "=" + x.Value))}; " +
                               $"Layer: {string.Join(", ", layers.OrderByDescending(x => x.Value).Take(6).Select(x => x.Key + "=" + x.Value))}; " +
                               $"Schatten: {string.Join(", ", casts.Select(x => x.Key + "=" + x.Value))}");
            foreach (var light in FindObjectsOfType<Light>().Where(x => x.type == LightType.Directional))
                Plugin.Log.LogInfo($"The Compound Sonne: {light.name} aktiv={light.isActiveAndEnabled} Intensitaet={light.intensity:F2} Schatten={light.shadows} Staerke={light.shadowStrength:F2} " +
                                   $"Maske=0x{light.cullingMask:X8} Szene={light.gameObject.scene.name}");
            var stencils = FindObjectsOfType<StencilShadow>().Count(x => (x.gameObject.scene.name ?? "").StartsWith("OBM_TC"));
            var ambient = FindObjectsOfType<AmbientLight>();
            Plugin.Log.LogInfo($"The Compound Innenraeume: {stencils} StencilShadow-Raeume, AmbientLight aktiv: {ambient.Count(a => a.isActiveAndEnabled)}/{ambient.Length}");
            Plugin.Log.LogInfo($"The Compound Schatten: Qualitaet={QualitySettings.shadows} Distanz={QualitySettings.shadowDistance} Kaskaden={QualitySettings.shadowCascades}; " +
                               "Tasten: Strg+Alt+1 Tarkov-Shader, Strg+Alt+2 Lightmaps, Strg+Alt+3 Tageszeit-Tint, Strg+Alt+4 Innen-Abdunklung, Strg+Alt+5 Innen-Umgebungslicht");
        }

        void Update()
        {
            if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) || !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) { _tarkov = !_tarkov; ApplyMaterials(); }
            if (Input.GetKeyDown(KeyCode.Alpha2)) { _noLightmaps = !_noLightmaps; ApplyLightmaps(); }
            if (Input.GetKeyDown(KeyCode.Alpha3)) { _noTint = !_noTint; CompoundLightsAndSounds.TintDisabled = _noTint; }
            if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                _stencilIndex = (_stencilIndex + 1) % StencilSteps.Length;
                var n = 0;
                foreach (var ss in FindObjectsOfType<StencilShadow>())
                {
                    if (!(ss.gameObject.scene.name ?? "").StartsWith("OBM_TC") || ss.Ambient.a > 0.94f && _stencilIndex != 2 && ss.name.Contains("Keller")) continue;
                    var c = ss.Ambient; c.a = StencilSteps[_stencilIndex]; ss.Ambient = c; n++;
                }
                Plugin.Log.LogInfo($"The Compound Shader-Test: Innen-Umgebungslicht (StencilShadow) {StencilSteps[_stencilIndex]:F2} auf {n} Raeumen");
            }
            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                _darkIndex = (_darkIndex + 1) % DarkSteps.Length;
                Shader.SetGlobalVector(IndoorDarken, new Vector4(DarkSteps[_darkIndex], 0, 0, 1));
                Plugin.Log.LogInfo($"The Compound Shader-Test: Innen-Abdunklung {DarkSteps[_darkIndex]:F2}");
            }
        }

        void ApplyMaterials()
        {
            int swapped = 0;
            foreach (var (r, orig, lightmap) in _renderers)
            {
                if (r == null) continue;
                if (!_tarkov) { r.sharedMaterials = orig; continue; }
                var mats = (Material[])orig.Clone();
                for (var i = 0; i < mats.Length; i++)
                {
                    var t = ToTarkov(mats[i], lightmap >= 0 && lightmap < 0xFFFE && !_noLightmaps);
                    if (t != mats[i]) { mats[i] = t; swapped++; }
                }
                r.sharedMaterials = mats;
            }
            Plugin.Log.LogInfo($"The Compound Shader-Test: {(_tarkov ? "Tarkov-Shader AN" : "Tarkov-Shader AUS")} ({swapped} Materialplaetze)");
        }

        Material ToTarkov(Material m, bool baked)
        {
            if (m == null || m.shader == null || !Ours.Contains(m.shader.name) || m.renderQueue >= 3000) return m;
            var cutout = m.IsKeywordEnabled("_ALPHATEST_ON") || m.shader.name.Contains("Cutout") || m.renderQueue >= 2450;
            var shader = cutout ? _p0Cutout : baked && _p0Baked != null ? _p0Baked : _p0;
            if (shader == null) return m;
            if (_cache.TryGetValue((m, baked), out var done)) return done;
            var t = new Material(shader) { name = m.name + " (Tarkov)", renderQueue = cutout ? 2450 : 2000 };
            if (m.HasProperty("_MainTex"))
            {
                t.SetTexture("_MainTex", m.GetTexture("_MainTex"));
                t.SetTextureScale("_MainTex", m.GetTextureScale("_MainTex"));
                t.SetTextureOffset("_MainTex", m.GetTextureOffset("_MainTex"));
            }
            if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null) t.SetTexture("_BumpMap", m.GetTexture("_BumpMap"));
            if (m.HasProperty("_Color")) t.SetColor("_Color", m.GetColor("_Color"));
            t.SetFloat("_StencilType", 0f);
            if (cutout) t.SetFloat("_Cutoff", m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.45f);
            else
            {
                // albedo alpha is the specular mask in Tarkov's shader, keep it low for mostly opaque Siege textures
                t.SetColor("_SpecColor", new Color(0.25f, 0.25f, 0.25f, 1f));
                if (t.HasProperty("_ReflectColor")) t.SetColor("_ReflectColor", new Color(0.35f, 0.35f, 0.35f, 0.35f));
            }
            _cache[(m, baked)] = t;
            return t;
        }

        void ApplyLightmaps()
        {
            foreach (var (r, _, lightmap) in _renderers)
                if (r != null) r.lightmapIndex = _noLightmaps ? -1 : lightmap;
            if (_tarkov) ApplyMaterials(); // baked / unbaked variant
            Plugin.Log.LogInfo("The Compound Shader-Test: Lightmaps " + (_noLightmaps ? "AUS" : "AN"));
        }
    }
}


namespace OBMTheCompound.Client
{
    // bot debug log every 60 s: alive roles and who is hostile to whom
    public class CompoundBotDiag : MonoBehaviour
    {
        string _last = "";

        void Start() => InvokeRepeating(nameof(Check), 45f, 60f);

        void Check()
        {
            try
            {
                var bots = FindObjectsOfType<BotOwner>().Where(b => b != null && !b.IsDead && b.GetPlayer != null && b.BotsGroup != null).ToList();
                if (bots.Count == 0) return;
                string Role(BotOwner b) => b.Profile.Info.Settings.Role.ToString();
                var lines = new System.Collections.Generic.List<string>();
                foreach (var g in bots.GroupBy(Role))
                {
                    var b = g.First();
                    var others = bots.Where(o => o != b).GroupBy(Role).Select(o => $"{o.Key}={(b.BotsGroup.IsEnemy(o.First().GetPlayer) ? "Feind" : "freundlich")}");
                    lines.Add($"{g.Key} x{g.Count()} (Gruppe {b.BotsGroup.MembersCount}, kennt {b.BotsGroup.Enemies.Count} Feinde, Zustand {b.BotState}): {string.Join(" ", others)}");
                }
                var text = string.Join(" | ", lines);
                if (text == _last) return;
                _last = text;
                Plugin.Log.LogInfo("The Compound Bots: " + text);
            }
            catch (Exception e) { Plugin.Log.LogWarning("The Compound Bots: Diagnose fehlgeschlagen: " + e.Message); }
        }
    }
}


namespace OBMTheCompound.Client
{
    // BotOwner.method_10 activates the bot in a try/catch that swallows the exception and sets ActiveFail.
    // The bot then stands still without a weapon and can't die, and nothing gets logged.
    // This transpiler replaces the pop at the start of the catch block with a log call.
    public static class BotActivateDiag
    {
        static int _count;

        public static void Apply(ManualLogSource log)
        {
            try
            {
                var target = AccessTools.Method(typeof(BotOwner), "method_10");
                if (target == null) { log.LogWarning("The Compound: BotOwner.method_10 nicht gefunden (Bot-Diagnose aus)"); return; }
                new Harmony("private.obm.thecompound.client.botdiag").Patch(target,
                    transpiler: new HarmonyMethod(typeof(BotActivateDiag), nameof(Transpiler)));
            }
            catch (Exception e) { log.LogWarning("The Compound: Bot-Diagnose nicht aktiv: " + e.Message); }
        }

        static System.Collections.Generic.IEnumerable<CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<CodeInstruction> instructions)
        {
            var log = AccessTools.Method(typeof(BotActivateDiag), nameof(Report));
            foreach (var ins in instructions)
            {
                if (ins.opcode == System.Reflection.Emit.OpCodes.Pop && ins.blocks.Any(b => b.blockType == ExceptionBlockType.BeginCatchBlock))
                {
                    var call = new CodeInstruction(System.Reflection.Emit.OpCodes.Call, log);
                    call.blocks.AddRange(ins.blocks);
                    call.labels.AddRange(ins.labels);
                    yield return call;
                    continue;
                }
                yield return ins;
            }
        }

        public static void Report(Exception e)
        {
            if (_count++ >= 8) return;
            var trace = (e.StackTrace ?? "").Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Take(10);
            Plugin.Log.LogWarning($"The Compound Bot-Start fehlgeschlagen: {e.GetType().Name}: {e.Message} | {string.Join(" < ", trace)}");
        }
    }
}


namespace OBMTheCompound.Client
{
    // Respawns per bot zone (obm-botzones.json from the Unity build). Each zone spawns once at raid start,
    // afterwards bots of the zone's types come back after a random pause until maxAlive is reached.
    // Zones marked once never respawn. Only runs where the bots are simulated.
    public class CompoundBotRespawn : MonoBehaviour
    {
        class Spawn { public string type; public int min = 1, max = 1, chance = 100; }
        class Zone { public string zone; public bool once, respawn; public float minMinutes = 3, maxMinutes = 8; public int maxAlive = 4; public List<Spawn> spawns = new List<Spawn>(); public float next = -1; }
        readonly List<Zone> _zones = new List<Zone>();

        void Start()
        {
            try
            {
                var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "", "obm-botzones.json");
                if (!System.IO.File.Exists(file)) { Plugin.Log.LogInfo("The Compound Nachspawn: keine obm-botzones.json (Map neu bauen)"); return; }
                var root = SimpleJson.Parse(System.IO.File.ReadAllText(file)) as Dictionary<string, object>;
                foreach (Dictionary<string, object> z in (List<object>)root["zones"])
                {
                    var zone = new Zone
                    {
                        zone = (string)z["zone"], once = (bool)z["once"], respawn = (bool)z["respawn"],
                        minMinutes = Convert.ToSingle(z["minMinutes"]), maxMinutes = Convert.ToSingle(z["maxMinutes"]), maxAlive = Convert.ToInt32(z["maxAlive"]),
                    };
                    foreach (Dictionary<string, object> e in (List<object>)z["spawns"])
                        zone.spawns.Add(new Spawn { type = (string)e["type"], min = Convert.ToInt32(e["min"]), max = Convert.ToInt32(e["max"]), chance = Convert.ToInt32(e["chance"]) });
                    _zones.Add(zone);
                }
                Plugin.Log.LogInfo("The Compound Nachspawn: " + string.Join(", ", _zones.Select(z => $"{z.zone} {(z.once ? "nur einmal" : z.respawn ? $"max {z.maxAlive}, alle {z.minMinutes:0.#}-{z.maxMinutes:0.#} min" : "kein Nachspawn")}")));
                InvokeRepeating(nameof(Tick), 20f, 5f);
            }
            catch (Exception e) { Plugin.Log.LogWarning("The Compound Nachspawn: obm-botzones.json nicht lesbar: " + e.Message); }
        }

        void Tick()
        {
            try
            {
                var bc = Singleton<IBotGame>.Instance?.BotsController;
                if (bc?.BotSpawner?.AllBotZones == null) return;
                foreach (var z in _zones)
                {
                    if (z.once || !z.respawn || z.spawns.Count == 0) continue;
                    var botZone = bc.BotSpawner.AllBotZones.FirstOrDefault(b => b != null && b.NameZone == z.zone);
                    if (botZone == null) continue;
                    var alive = bc.Bots.GetListByZone(botZone).Count(b => b != null && !b.IsDead);
                    if (alive >= z.maxAlive) { z.next = -1; continue; }
                    // zone is missing bots: wait min..max minutes, then spawn one group
                    if (z.next < 0) { z.next = Time.time + UnityEngine.Random.Range(z.minMinutes, z.maxMinutes) * 60f; continue; }
                    if (Time.time < z.next) continue;
                    z.next = -1;
                    var pick = Pick(z.spawns);
                    if (pick == null) continue;
                    var count = Mathf.Clamp(UnityEngine.Random.Range(pick.min, pick.max + 1), 1, z.maxAlive - alive);
                    if (!Enum.TryParse(pick.type, true, out WildSpawnType role)) { Plugin.Log.LogWarning("The Compound Nachspawn: unbekannter Typ " + pick.type); continue; }
                    // PMCs, raiders, rogues and bosses use a copy of the map's boss spawn entry (escorts, gear),
                    // plain waves only work for scavs
                    if (!WaveTypes.Contains(pick.type) && BossRespawn(bc, z.zone, pick.type, count))
                    {
                        Plugin.Log.LogInfo($"The Compound Nachspawn: {pick.type}-Gruppe ({count}) in {z.zone} ({alive}/{z.maxAlive} lebten)");
                        continue;
                    }
                    var side = role == WildSpawnType.pmcUSEC ? EPlayerSide.Usec : role == WildSpawnType.pmcBEAR ? EPlayerSide.Bear : EPlayerSide.Savage;
                    bc.ActivateBotsByWave(new BotWaveDataClass
                    {
                        BotsCount = count, Side = side, SpawnAreaName = z.zone, Time = Time.time, WildSpawnType = role,
                        IsPlayers = false, Difficulty = BotDifficulty.normal, ChanceGroup = 0f, WithCheckMinMax = false, KeepZoneOnSpawn = true,
                    }).ContinueWith(t => { if (t.Exception != null) Plugin.Log.LogWarning("The Compound Nachspawn: " + t.Exception.GetBaseException().Message); });
                    Plugin.Log.LogInfo($"The Compound Nachspawn: {count}x {pick.type} in {z.zone} ({alive}/{z.maxAlive} lebten)");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("The Compound Nachspawn: " + e.Message); }
        }

        static readonly HashSet<string> WaveTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "assault", "marksman", "cursedAssault", "assaultGroup", "crazyAssaultEvent" };

        static bool BossRespawn(BotsController bc, string zone, string type, int count)
        {
            var game = Singleton<IBotGame>.Instance;
            var scenario = game == null ? null : AccessTools.Property(game.GetType(), "BossSpawnScenario")?.GetValue(game, null) as BossSpawnScenario;
            var original = scenario?.BossSpawnWaves?.FirstOrDefault(w => w != null && string.Equals(w.BossName, type, StringComparison.OrdinalIgnoreCase)
                                                                         && (w.BossZone ?? "").Split(',').Contains(zone));
            if (original == null) return false;
            var wave = original.Copy();
            wave.BossChance = 100f; wave.Time = -1f; wave.Delay = 0f; wave.BossZone = zone;
            wave.BossEscortAmount = Math.Max(0, count - 1).ToString();
            wave.Supports = null; // escorts only via BossEscortAmount, keeps the zone limit
            wave.Init();
            wave.ShallSpawn = true;
            bc.ActivateBotsByWave(wave);
            return true;
        }

        static Spawn Pick(List<Spawn> spawns)
        {
            var total = spawns.Sum(s => Math.Max(0, s.chance));
            if (total <= 0) return null;
            var r = UnityEngine.Random.Range(0, total);
            foreach (var s in spawns) { r -= Math.Max(0, s.chance); if (r < 0) return s; }
            return spawns[spawns.Count - 1];
        }
    }

    // small JSON reader for our own data files
    static class SimpleJson
    {
        public static object Parse(string json) { var i = 0; return Value(json, ref i); }

        static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            var c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                for (Skip(s, ref i); s[i] != '}'; Skip(s, ref i))
                {
                    var key = (string)Value(s, ref i); Skip(s, ref i); i++; // ':'
                    d[key] = Value(s, ref i); Skip(s, ref i);
                    if (s[i] == ',') i++;
                }
                i++; return d;
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                for (Skip(s, ref i); s[i] != ']'; Skip(s, ref i))
                {
                    l.Add(Value(s, ref i)); Skip(s, ref i);
                    if (s[i] == ',') i++;
                }
                i++; return l;
            }
            if (c == '"')
            {
                var sb = new System.Text.StringBuilder(); i++;
                while (s[i] != '"') { if (s[i] == '\\') i++; sb.Append(s[i++]); }
                i++; return sb.ToString();
            }
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            var start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
