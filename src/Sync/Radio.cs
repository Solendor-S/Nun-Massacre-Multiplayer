using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using NunMP.Net;
using UnityEngine;

namespace NunMP.Sync
{
    // Walkie-talkies: one per survivor spawns at seeded random spots (same on every machine). Pick one up, hold B
    // to talk to everyone else carrying one. Receiving radios crackle out loud - the nun can hear them.
    public static class Radio
    {
        public const byte TakenEv = 50;
        public static bool HasRadio;
        public static KeyCode Key = KeyCode.B;
        static readonly List<GameObject> radios = new List<GameObject>();

        public static void Init()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, _) => { HasRadio = false; radios.Clear(); };
            Session.On(Msg.Event, (slot, r) =>
            {
                if (!GameFlow.InGame || r.ReadByte() != TakenEv) return;
                Hide(r.ReadByte());
            });
        }

        static void Hide(int i) { if (i < radios.Count && radios[i]) radios[i].SetActive(false); }

        // Called from WorldSync.Build (before IDs), after the lighters: one radio per survivor, in the drawer
        // right below the main lighter's (or the same drawer if there's none), shrunk to fit.
        public static void Spawn()
        {
            int n = WorldSync.Survivors;
            var tpl = Object.FindObjectsOfType<ItemInteractable>().FirstOrDefault(i => i.name == "Radar Item" && i.transform.root.name == "Scripted Main Game OFF");
            var lighter = WorldSync.MainLighter();
            if (n <= 0 || !tpl || !lighter) { Plugin.Log.LogWarning($"radios: n={n} model={(bool)tpl} lighter={(bool)lighter}"); return; }
            var ldrawer = lighter.transform.parent;
            var drawer = ldrawer.parent ? ldrawer.parent.Cast<Transform>()
                .Where(t => t != ldrawer && t.GetComponent<CabinetsInteractable>() && t.GetComponent<Collider>() && t.position.y < ldrawer.position.y)
                .OrderBy(t => ldrawer.position.y - t.position.y).FirstOrDefault() : null;
            if (!drawer) drawer = ldrawer;
            var dcol = drawer.GetComponent<Collider>();
            var floor = dcol ? dcol.bounds.min.y + 0.03f : drawer.position.y;
            var start = new Vector3(lighter.transform.position.x, floor, lighter.transform.position.z);
            for (int i = 0; i < n; i++)
            {
                var go = Object.Instantiate(tpl.gameObject, drawer);
                go.name = "NunMP Radio " + i;
                foreach (var c in go.GetComponents<Component>())
                    if (!(c is Transform || c is MeshFilter || c is MeshRenderer || c is Collider)) Object.DestroyImmediate(c);
                foreach (Transform ch in go.transform) Object.Destroy(ch.gameObject);
                // shrink to drawer size (~22 cm)
                var r = go.GetComponent<Renderer>();
                if (r) { float big = Mathf.Max(r.bounds.size.x, r.bounds.size.y, r.bounds.size.z); if (big > 0.22f) go.transform.localScale *= 0.22f / big; }
                go.transform.rotation = drawer.rotation * Quaternion.Euler(0, 90, 0);
                var p = WorldSync.DrawerSpot(drawer, start, i, 0.22f);
                go.transform.position = p + Vector3.up * (r ? r.bounds.extents.y : 0.03f);
                var rp = go.AddComponent<RadioPickup>(); rp.Index = i; rp.InteractionString = "WALKIE-TALKIE";
                radios.Add(go);
            }
            Plugin.Log.LogInfo($"spawned {radios.Count} radio(s) in {drawer.name}");
        }

        public static void Take(int i)
        {
            if (HasRadio || NunMode.IAmNun) return;
            HasRadio = true; Hide(i);
            Session.Broadcast(Msg.Event, w => { w.Write(TakenEv); w.Write((byte)i); }, true);
            Plugin.Log.LogInfo("picked up a radio");
        }

        public static string Debug => $"hasRadio={HasRadio} radios={string.Join(",", radios.Select(r => r && r.activeSelf ? "1" : "0"))}";
    }

    public class RadioPickup : Interactable
    {
        public int Index;
        public override void Interact() => Radio.Take(Index);
    }
}
