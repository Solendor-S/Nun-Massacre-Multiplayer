using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using UnityEngine;

namespace NunMP.Sync
{
    // Physics doors/furniture: whoever drags it streams its pose (10 Hz) until it settles; others follow kinematically.
    public class DoorSync : MonoBehaviour
    {
        class Owned { public float until, lastMove; public Vector3 p; public Quaternion r; }
        class Follow { public Vector3 p; public Quaternion r; public float until; public bool wasKinematic; }
        static readonly Dictionary<MoveInteractable, Owned> mine = new Dictionary<MoveInteractable, Owned>();
        static readonly Dictionary<MoveInteractable, Follow> theirs = new Dictionary<MoveInteractable, Follow>();
        float next, nextScan;
        static MoveInteractable[] all;
        static readonly Dictionary<MoveInteractable, Quaternion> seen = new Dictionary<MoveInteractable, Quaternion>();

        // Most doors are opened by walking into them (physics), not by Interact: any door that moves near me
        // (or near the nun, on the host — her DoorOpener) is mine to stream.
        void DetectPushes()
        {
            if (Time.time < nextScan) return;
            nextScan = Time.time + 0.1f;
            if (all == null) { all = FindObjectsOfType<MoveInteractable>(); seen.Clear(); foreach (var m in all) if (m) seen[m] = m.transform.rotation; }
            var me = PlayerSync.Local ? PlayerSync.Local.transform.position : Vector3.one * 1e6f;
            bool driveNun = NunSync.Killer && (NunMode.IAmNun || (Net.Session.IsHost && !NunMode.Active));
            var nun = driveNun ? NunSync.Killer.transform.position : Vector3.one * 1e6f;
            foreach (var m in all)
            {
                if (!m) continue;
                var r = m.transform.rotation;
                if (Quaternion.Angle(r, seen[m]) < 0.5f) continue;
                seen[m] = r;
                if (theirs.ContainsKey(m) || mine.ContainsKey(m)) continue;
                var p = m.transform.position;
                if ((p - me).sqrMagnitude < 16f || (p - nun).sqrMagnitude < 16f) LocalDrag(m);
            }
        }

        public static string Describe(MoveInteractable m) => !m ? "" : mine.ContainsKey(m) ? "sync=mine" : theirs.ContainsKey(m) ? "sync=following" : "sync=free";

        public static void LocalDrag(MoveInteractable m)
        {
            if (!mine.ContainsKey(m)) mine[m] = new Owned();
            mine[m].until = Time.time + 3f;
            if (theirs.TryGetValue(m, out var f)) { Release(m, f); theirs.Remove(m); } // I took it over
        }

        public static void Remote(MoveInteractable m, Vector3 p, Quaternion r)
        {
            if (mine.TryGetValue(m, out var o))
            {
                if (m.ismoving || Time.time - o.lastMove < 0.4f) return; // I am actively moving it
                mine.Remove(m);                                           // resting: they take over
            }
            if (!theirs.TryGetValue(m, out var f))
            {
                var rb = m.GetComponent<Rigidbody>();
                theirs[m] = f = new Follow { wasKinematic = rb && rb.isKinematic };
                if (rb) rb.isKinematic = true;
            }
            f.p = p; f.r = r; f.until = Time.time + 1.5f;
        }

        static void Release(MoveInteractable m, Follow f)
        {
            var rb = m ? m.GetComponent<Rigidbody>() : null;
            if (rb) rb.isKinematic = f.wasKinematic;
        }

        void Update()
        {
            if (!GameFlow.InGame) { mine.Clear(); theirs.Clear(); all = null; return; }
            DetectPushes();
            foreach (var kv in theirs.ToList())
            {
                if (!kv.Key) { theirs.Remove(kv.Key); continue; }
                var t = kv.Key.transform;
                t.position = Vector3.Lerp(t.position, kv.Value.p, 15f * Time.deltaTime);
                t.rotation = Quaternion.Slerp(t.rotation, kv.Value.r, 15f * Time.deltaTime);
                if (Time.time > kv.Value.until) { Release(kv.Key, kv.Value); theirs.Remove(kv.Key); }
            }
            if (Time.time < next) return;
            next = Time.time + 0.1f;
            foreach (var kv in mine.ToList())
            {
                var m = kv.Key; var o = kv.Value;
                if (!m || Time.time > o.until) { mine.Remove(m); continue; }
                var t = m.transform;
                if (m.ismoving) o.until = Time.time + 1.5f; // keep streaming while held + settle time
                if ((t.position - o.p).sqrMagnitude < 1e-6f && Quaternion.Angle(t.rotation, o.r) < 0.2f) continue;
                o.p = t.position; o.r = t.rotation; o.lastMove = Time.time; o.until = Mathf.Max(o.until, Time.time + 1f);
                var id = WorldSync.Id(m);
                if (id == null) continue;
                WorldSync.Send(WEv.Move, w =>
                {
                    w.Write(id);
                    w.Write(o.p.x); w.Write(o.p.y); w.Write(o.p.z);
                    w.Write(o.r.x); w.Write(o.r.y); w.Write(o.r.z); w.Write(o.r.w);
                });
            }
        }
    }
}
