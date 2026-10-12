using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace OBMTheCompound.Client
{
    // Destructible walls. The Unity build splits marked walls into tiles under "OBM_Destructible:<Kind>:<Strength>".
    // Explosions remove the tiles in a radius around the blast and turn them into debris.
    public static class CompoundDestruction
    {
        class Area { public Bounds bounds; public float strength; public List<Transform> tiles; }
        static readonly List<Area> Areas = new List<Area>();
        static Vector3 _lastPos; static float _lastTime = -10;
        static int _debrisLayer = -1;

        public static void Patch(Harmony harmony)
        {
            int n = 0;
            // static Explosion(IExplosiveItem, Vector3, ...) used by all explosives
            foreach (var m in typeof(GameWorld).Assembly.GetTypes().SelectMany(t => { try { return t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly); } catch { return new MethodInfo[0]; } }))
            {
                var p = m.GetParameters();
                if (m.Name != "Explosion" || p.Length < 2 || p[0].ParameterType != typeof(IExplosiveItem) || p[1].ParameterType != typeof(Vector3)) continue;
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(CompoundDestruction), nameof(ItemPostfix))); n++;
            }
            // bot event handlers as fallback (position only)
            foreach (var name in new[] { "GrenadeExplosion", "RocketExplosion" })
            {
                var m = AccessTools.Method(typeof(BotEventHandler), name);
                if (m != null) { harmony.Patch(m, postfix: new HarmonyMethod(typeof(CompoundDestruction), nameof(PositionPostfix))); n++; }
            }
            Plugin.Log.LogInfo($"The Compound: Zerstoerung an {n} Explosions-Methoden angeschlossen");
        }

        static void ItemPostfix(IExplosiveItem __0, Vector3 __1)
        {
            float max = 5f;
            try { if (__0 != null) max = __0.MaxExplosionDistance; } catch { }
            Explode(__1, max);
        }

        static void PositionPostfix(Vector3 __0) => Explode(__0, 5f);

        public static void Collect()
        {
            Areas.Clear();
            foreach (var root in Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt).Where(s => s.isLoaded).SelectMany(s => s.GetRootGameObjects()))
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("OBM_Destructible:", StringComparison.Ordinal)) continue;
                    var parts = t.name.Split(':');
                    var area = new Area { strength = parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1f,
                                          tiles = t.Cast<Transform>().Where(c => c.GetComponent<Renderer>() != null).ToList() };
                    if (area.tiles.Count == 0) continue;
                    area.bounds = area.tiles[0].GetComponent<Renderer>().bounds;
                    foreach (var tile in area.tiles) area.bounds.Encapsulate(tile.GetComponent<Renderer>().bounds);
                    Areas.Add(area);
                }
            _debrisLayer = new[] { "Shells", "Debris", "Ignore Raycast" }.Select(LayerMask.NameToLayer).FirstOrDefault(l => l >= 0);
            Plugin.Log.LogInfo($"The Compound: {Areas.Count} zerstoerbare Waende, {Areas.Sum(a => a.tiles.Count)} Kacheln");
        }

        static void Explode(Vector3 position, float maxDistance)
        {
            if (Areas.Count == 0) return;
            // one blast is reported by several hooks
            if (Time.time - _lastTime < 0.25f && (position - _lastPos).sqrMagnitude < 1f) return;
            _lastTime = Time.time; _lastPos = position;
            var radius = Mathf.Clamp(maxDistance * 0.12f, 0.45f, 1.2f); // ~0.6 m for a hand grenade
            var broken = 0;
            foreach (var area in Areas)
            {
                var r = radius * area.strength;
                if (area.bounds.SqrDistance(position) > r * r) continue;
                foreach (var tile in area.tiles)
                {
                    if (tile == null || !tile.gameObject.activeSelf) continue;
                    var b = tile.GetComponent<Renderer>().bounds;
                    if (b.SqrDistance(position) > r * r) continue;
                    Break(tile, position, r);
                    broken++;
                }
            }
            if (broken > 0) Plugin.Log.LogInfo($"The Compound: Explosion bei {position}, {broken} Kacheln zerstoert (Radius {radius:F1} m)");
        }

        static void Break(Transform tile, Vector3 blast, float radius)
        {
            try
            {
                var mf = tile.GetComponent<MeshFilter>(); var mr = tile.GetComponent<MeshRenderer>();
                if (mf != null && mr != null && mf.sharedMesh != null)
                {
                    var debris = new GameObject("OBM_Debris");
                    debris.transform.SetPositionAndRotation(tile.position, tile.rotation);
                    debris.transform.localScale = tile.lossyScale;
                    debris.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var r = debris.AddComponent<MeshRenderer>(); r.sharedMaterials = mr.sharedMaterials;
                    var box = debris.AddComponent<BoxCollider>(); box.center = mf.sharedMesh.bounds.center; box.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.02f);
                    if (_debrisLayer >= 0) debris.layer = _debrisLayer;
                    var rb = debris.AddComponent<Rigidbody>(); rb.mass = 1.5f;
                    rb.AddExplosionForce(6f, blast, radius * 3f, 0.4f, ForceMode.Impulse);
                    UnityEngine.Object.Destroy(debris, 30f);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("The Compound: Truemmer: " + e.Message); }
            tile.gameObject.SetActive(false);
        }
    }
}
