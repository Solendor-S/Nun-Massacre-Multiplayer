using System.Collections.Generic;
using GrannyClone;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NunMP.Sync
{
    // The radar shows the nun as a blinking red blip. Teammates get a steady blue copy of that blip, placed with
    // the radar's own math (offset from you * radarZoom, inside the same rotating circle).
    [HarmonyPatch(typeof(Radar), nameof(Radar.mecUpdate))]
    static class RadarFriends
    {
        static readonly Color Friend = new Color(0.3f, 0.9f, 1f); // bright cyan: reads through the radar's amber screen
        static readonly Dictionary<Radar, Dictionary<byte, Image>> blips = new Dictionary<Radar, Dictionary<byte, Image>>();

        static void Postfix(Radar __instance)
        {
            if (!__instance.blip || !__instance.center) return;
            if (!blips.TryGetValue(__instance, out var mine)) blips[__instance] = mine = new Dictionary<byte, Image>();
            var c = __instance.center.position;
            foreach (var kv in PlayerSync.Puppets)
            {
                if (!mine.TryGetValue(kv.Key, out var img) || !img)
                {
                    var go = Object.Instantiate(__instance.blip.gameObject, __instance.blip.parent);
                    go.name = "NunMP Friend Blip " + kv.Key;
                    mine[kv.Key] = img = go.GetComponent<Image>();
                    img.color = Friend; go.transform.localScale *= 1.6f;
                }
                var p = kv.Value;
                bool show = GameFlow.InGame && p && (p.Flags & PlayerFlags.Dead) == 0;
                img.enabled = show;
                if (!show) continue;
                var t = p.transform.position;
                img.rectTransform.localPosition = new Vector3((c.x - t.x) * __instance.radarZoom, (c.z - t.z) * __instance.radarZoom, 0f);
            }
            foreach (var kv in mine) if (kv.Value && !PlayerSync.Puppets.ContainsKey(kv.Key)) kv.Value.enabled = false; // left the game
        }
    }
}
