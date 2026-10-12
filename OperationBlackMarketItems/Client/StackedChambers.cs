using System.Collections.Generic;
using UnityEngine;

namespace R6PortClient
{
    // BOSG 12.2 on the MR43 rig. The MR43 has side by side chambers, the BOSG has stacked barrels.
    // The Unity build adds mod_barrel/OBM_StackedChambers with R_<bone> (MR43 position) and T_<bone> (BOSG chamber).
    // After the animator ran, shells close to a chamber are moved to the BOSG chamber. The shift fades out
    // towards the hand so reload animations still work.
    public class StackedChambers : MonoBehaviour
    {
        const float Full = 0.015f, None = 0.08f; // full shift within 15 mm, none past 80 mm
        readonly List<(Transform bone, Vector3 rest, Vector3 delta)> bones = new List<(Transform, Vector3, Vector3)>();
        readonly Dictionary<Transform, (int frame, Vector3 animated, Vector3 written)> state = new Dictionary<Transform, (int, Vector3, Vector3)>();

        internal void Bind()
        {
            bones.Clear();
            var barrel = transform.parent;
            foreach (Transform r in transform)
            {
                if (!r.name.StartsWith("R_")) continue;
                var name = r.name.Substring(2);
                var t = transform.Find("T_" + name);
                var bone = barrel.Find(name);
                if (t != null && bone != null) bones.Add((bone, r.localPosition, t.localPosition - r.localPosition));
            }
            Plugin.Log.LogInfo("OBM BOSG: " + bones.Count + " Patronen-/Auswurfpunkte uebereinander");
        }

        void OnEnable() { Camera.onPreCull += Apply; }
        void OnDisable() { Camera.onPreCull -= Apply; }
        void LateUpdate() { Apply(null); }

        // LateUpdate and before each camera render, safe to run twice per frame
        void Apply(Camera _)
        {
            int frame = Time.frameCount;
            foreach (var (bone, rest, delta) in bones)
            {
                if (bone == null) continue;
                var current = bone.localPosition;
                Vector3 animated;
                if (state.TryGetValue(bone, out var s) && (s.frame == frame || current == s.written)) animated = s.animated;
                else animated = current;
                float distance = Vector3.Distance(animated, rest);
                float weight = 1f - Mathf.Clamp01((distance - Full) / (None - Full));
                var written = animated + delta * weight;
                bone.localPosition = written;
                state[bone] = (frame, animated, written);
            }
        }
    }
}
