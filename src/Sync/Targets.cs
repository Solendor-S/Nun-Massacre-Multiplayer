using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using HarmonyLib;
using NodeCanvas.Framework;
using NunMP.Net;
using UnityEngine;

namespace NunMP.Sync
{
    public enum Ev : byte { Noise = 2, BedHide = 30, FlipBed = 31 }

    // Host-side "who is the nun after". Remote players get a Dummy (sight point + SightDetectable) on their puppet;
    // Harmony postfixes on the nun's sensing tasks evaluate dummies too and retarget her blackboard.
    public class Targets : MonoBehaviour
    {
        public const byte LocalSlot = 255;
        public static byte Current = LocalSlot;
        static float lastSpot = -99;
        static Blackboard bb;
        static Transform localPlayer, localHit;
        static byte applied = LocalSlot;

        public static bool HostMP => Session.IsHost && GameFlow.InGame && !NunMode.Active; // no AI in Play-as-Nun
        public static Dummy CurDummy => Current != LocalSlot && PlayerSync.Puppets.TryGetValue(Current, out var p) && p ? p.Dummy : null;
        public static IEnumerable<Dummy> Dummies => PlayerSync.Puppets.Values.Where(p => p && p.Dummy != null && p.Dummy.Eligible).Select(p => p.Dummy);

        public static void Init()
        {
            // Damage is client-side: each peer's own copy of the nun (real or puppet) swings at its local player.
            Message.HumanoidEvents.DamageEvent += (hit, pt, n, amount) =>
            {
                if (GameFlow.InGame && hit && PlayerSync.Local && hit == PlayerSync.Local.transform) Plugin.Log.LogInfo($"local player damaged {amount}");
            };
            Message.Detectable.NoiseMadeEvent += (pos, radius) =>
            {   // client noise -> host's NoiseAlertMaster
                if (!GameFlow.InGame || Session.IsHost || relaying) return;
                Session.Broadcast(Msg.Event, w => { w.Write((byte)Ev.Noise); w.Write(pos.x); w.Write(pos.y); w.Write(pos.z); w.Write(radius); }, true);
            };
            // Remote players hiding under a bed: the host's nun needs to know which bed to flip.
            MessagesGranny.HidingPlayerEvent += (killerPt, bed) =>
            {
                if (!GameFlow.InGame || Session.IsHost) return;
                var id = WorldSync.Id(bed ? bed.GetComponent<HideBedInteractable>() : null) ?? "";
                Session.Broadcast(Msg.Event, w => { w.Write((byte)Ev.BedHide); w.Write(id); }, true);
            };
            MessagesGranny.HideExitEvent += () =>
            {
                if (!GameFlow.InGame || Session.IsHost) return;
                Session.Broadcast(Msg.Event, w => { w.Write((byte)Ev.BedHide); w.Write(""); }, true);
            };
            Session.On(Msg.Event, (slot, r) =>
            {
                if (!GameFlow.InGame) return;
                var ev = (Ev)r.ReadByte();
                switch (ev)
                {
                    case Ev.BedHide:
                        {
                            var id = r.ReadString();
                            if (!Session.IsHost || !PlayerSync.Puppets.TryGetValue(slot, out var pp) || !pp || pp.Dummy == null) break;
                            var d = pp.Dummy;
                            d.Bed = id.Length > 0 ? WorldSync.Find<HideBedInteractable>(id) : null;
                            // she saw them dive in -> stays on them and goes for the bed (like vanilla)
                            d.SeenHiding = d.Bed && Current == slot && Time.time - lastSpot < 3f;
                            d.BedSince = Time.time;
                            Plugin.Log.LogInfo($"slot {slot} {(d.Bed ? "hides under " + d.Bed.name + (d.SeenHiding ? " (she saw)" : "") : "left the bed")}");
                            break;
                        }
                    case Ev.FlipBed:
                        {
                            var bed = WorldSync.Find<HideBedInteractable>(r.ReadString());
                            float delay = r.ReadSingle(), dur = r.ReadSingle();
                            if (!Session.IsHost && bed) MessagesGranny.FlipBedEvent?.Invoke(bed.transform, delay, dur);
                            break;
                        }
                    case Ev.Noise:
                        var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); var rad = r.ReadSingle();
                        if (Session.IsHost) { if (Time.time > nextNoiseLog) { nextNoiseLog = Time.time + 2; Plugin.Log.LogInfo($"client noise r={rad:0.0} at {pos}"); } relaying = true; try { Message.Detectable.NoiseMadeEvent?.Invoke(pos, rad); } finally { relaying = false; } }
                        break;
                }
            });
        }
        static bool relaying;
        static float nextNoiseLog;

        // Called by sight patches with everyone visible this check.
        public static void Spotted(List<byte> seen)
        {
            if (seen.Count == 0) return;
            lastSpot = Time.time;
            if (seen.Contains(Current)) return;
            Current = seen.OrderBy(Dist).First();
        }

        static float Dist(byte slot)
        {
            var k = NunSync.Killer;
            if (!k) return 0;
            if (slot == LocalSlot) return localPlayer ? Vector3.Distance(k.transform.position, localPlayer.position) : 999;
            return PlayerSync.Puppets.TryGetValue(slot, out var p) && p ? Vector3.Distance(k.transform.position, p.transform.position) : 999;
        }

        public static string Debug => !HostMP ? "" :
            $"target={(Current == LocalSlot ? "host" : "slot " + Current)} spot={Time.time - lastSpot:0}s ago host={Dist(LocalSlot):0.0}m " +
            string.Join(" ", PlayerSync.Puppets.Values.Where(p => p && p.Dummy != null).Select(p => $"s{p.Slot}={Dist(p.Slot):0.0}m{(p.Dummy.Eligible ? "" : "(x)")} f={p.Flags}")) +
            " | " + SensePatches.LastSight;

        static bool LocalEligible => PlayerSync.Local && PlayerSync.Local.health && PlayerSync.Local.health.isAlive();

        void Update()
        {
            if (!HostMP || !NunSync.Killer) { bb = null; Current = applied = LocalSlot; return; }
            if (!bb)
            {
                bb = NunSync.Killer.GetComponent<Blackboard>();
                localPlayer = bb.GetVariable<Transform>("Player").value;
                localHit = bb.GetVariable<Transform>("Hit Target for killer").value;
            }
            // Not chasing anyone recently -> hunt whoever is closest (hearing/taunts/search all key off "Player").
            var cd = CurDummy;
            bool bedHunt = cd != null && cd.SeenHiding && cd.Bed && Time.time - cd.BedSince < 25f;
            foreach (var dd in Dummies.Concat(PlayerSync.Puppets.Values.Where(p => p && p.Dummy != null).Select(p => p.Dummy))) dd.Crouch((dd.P.Flags & PlayerFlags.Hiding) != 0);
            if (!bedHunt && (Time.time - lastSpot > 3f || (Current != LocalSlot && CurDummy == null) || (Current == LocalSlot && !LocalEligible)))
            {
                var all = Dummies.Select(d => d.Slot).ToList();
                if (LocalEligible) all.Add(LocalSlot);
                if (all.Count > 0) Current = all.OrderBy(Dist).First();
            }
            if (Current == applied) return;
            var d0 = CurDummy;
            bb.SetValue("Player", d0 != null ? d0.Root : localPlayer);
            bb.SetValue("Hit Target for killer", d0 != null ? d0.Head : localHit);
            applied = Current;
            Plugin.Log.LogInfo("nun target -> " + (d0 != null ? "slot " + Current : "host"));
        }
    }

    public class Dummy
    {
        public byte Slot;
        public Puppet P;
        public Transform Root, Head, Sight; // Sight = body capsule (layer Player): CanSeeTarget + weapon hits
        public Transform[] Points;          // CanSeeSightDetectable linecast targets
        public Collider Col;
        public Detectable.SightDetectable Sd;
        float nextScan;
        public bool Eligible => (P.Flags & (PlayerFlags.Dead | PlayerFlags.Hiding)) == 0;
        public HideBedInteractable Bed; public bool SeenHiding; public float BedSince;
        static readonly float[] standY = { 0.9f, 1.3f, 1.3f };
        // hiding under a bed: sight points drop low like the real player's shrunken controller
        public void Crouch(bool low) { if (Points == null) return; for (int i = 0; i < Points.Length; i++) { var lp = Points[i].localPosition; lp.y = low ? 0.25f : standY[i]; Points[i].localPosition = lp; } }
        public bool Flag(byte f) => (P.Flags & f) != 0;

        public static Dummy Attach(Puppet p)
        {
            int layer = LayerMask.NameToLayer("Player");
            var d = new Dummy { Slot = p.Slot, P = p, Root = p.transform };
            d.Sight = new GameObject("sight").transform;
            d.Sight.SetParent(p.transform, false);
            d.Sight.localPosition = new Vector3(0, 0.9f, 0);
            d.Sight.gameObject.layer = layer;
            var c = d.Sight.gameObject.AddComponent<CapsuleCollider>();
            c.isTrigger = true; c.radius = 0.35f; c.height = 1.8f;
            d.Col = c;
            d.Head = new GameObject("head").transform;
            d.Head.SetParent(p.transform, false);
            d.Head.localPosition = new Vector3(0, 1.6f, 0);
            // Same sight points as the real player: middle + shoulders, small spheres on layer CameraTrigger.
            int camLayer = LayerMask.NameToLayer("CameraTrigger");
            d.Points = new[] { new Vector3(0, 0.9f, 0), new Vector3(0.1f, 1.3f, 0), new Vector3(-0.1f, 1.3f, 0) }.Select((lp, i) =>
            {
                var t = new GameObject("sightpt" + i).transform;
                t.SetParent(p.transform, false); t.localPosition = lp; t.gameObject.layer = camLayer;
                var s = t.gameObject.AddComponent<SphereCollider>(); s.radius = 0.15f; s.isTrigger = true;
                return t;
            }).ToArray();
            var sdGo = new GameObject("sd"); sdGo.transform.SetParent(p.transform, false);
            sdGo.SetActive(false); // no Start(): it needs a CharacterController; we drive its illumination scan ourselves
            d.Sd = sdGo.AddComponent<Detectable.SightDetectable>();
            d.Sd.SightColliders = d.Points;
            return d;
        }

        public void Tick()
        {
            if (Time.time < nextScan) return;
            nextScan = Time.time + 0.25f;
            Sd.Illumination = 0;
            Message.Detectable.IlluminationScanEvent?.Invoke(Sd);
        }
    }

    // ---- Harmony: make the nun's senses consider remote players (host only) ----
    static class SensePatches
    {
        static readonly List<byte> seen = new List<byte>();
        public static string LastSight = "sight never checked";
        static int calls;

        [HarmonyPatch(typeof(Humanoid.Agent.EnemyHunter.Tasks.CanSeeSightDetectable), "OnCheck")]
        static class Sight
        {
            static void Postfix(Humanoid.Agent.EnemyHunter.Tasks.CanSeeSightDetectable __instance, ref bool __result)
            {
                if (!Targets.HostMP || __instance.Ignore.value || __instance.IgnoreDebug.value) return;
                seen.Clear();
                if (__result) seen.Add(Targets.LocalSlot);
                var a = __instance.agent;
                foreach (var d in Targets.Dummies)
                {
                    var tp = d.Sight.position;
                    float dist = Vector3.Distance(a.position, tp);
                    bool vis = dist < __instance.awarnessDistance.value;
                    var ang = Vector3.Angle(tp - a.position, a.forward);
                    string why = "far/angle";
                    if (!vis && dist <= __instance.maxDistance.value && ang < __instance.viewAngle.value)
                        foreach (var pt in d.Points)
                        {
                            var ok = Physics.Linecast(a.position + __instance.offsetAgent, pt.position + __instance.offsetTarget, out var hit, __instance.sightLayers.value);
                            why = ok ? "hit " + hit.collider.name : "no hit";
                            if (ok && hit.collider.transform == pt) { vis = true; break; }
                        }
                    if (vis) seen.Add(d.Slot);
                    LastSight = $"mask={string.Join("+", Enumerable.Range(0, 32).Where(i => (__instance.sightLayers.value.value & (1 << i)) != 0).Select(LayerMask.LayerToName))} calls={++calls} d={dist:0.0} ang={ang:0}/{__instance.viewAngle.value:0} max={__instance.maxDistance.value:0} {(vis ? "SEEN" : why)} local={__result}";
                }
                Targets.Spotted(seen);
                __result = seen.Count > 0;
            }
        }

        [HarmonyPatch(typeof(Humanoid.Agent.Tasks.CanSeeTarget), "OnCheck")]
        static class SeeTarget
        {
            static void Postfix(Humanoid.Agent.Tasks.CanSeeTarget __instance, ref bool __result)
            {
                var d = Targets.HostMP ? Targets.CurDummy : null;
                if (d == null) return;
                var a = __instance.agent;
                var tp = d.Sight.position;
                float dist = Vector3.Distance(a.position, tp);
                __result = dist < __instance.awarnessDistance.value ||
                    (dist <= __instance.maxDistance.value && Vector3.Angle(tp - a.position, a.forward) < __instance.viewAngle.value &&
                     Physics.Linecast(a.position + __instance.offsetAgent, tp + __instance.offsetTarget, out var hit) && hit.collider == d.Col);
            }
        }

        [HarmonyPatch(typeof(Humanoid.Agent.EnemyHunter.Tasks.CheckIlluminationSightDetectabke), "OnCheck")]
        static class Illum
        {
            static void Postfix(Humanoid.Agent.EnemyHunter.Tasks.CheckIlluminationSightDetectabke __instance, ref bool __result)
            {
                var d = Targets.HostMP ? Targets.CurDummy : null;
                if (d == null) return;
                d.Tick();
                var dist = Vector3.Distance(__instance.agent.position, d.Sight.position);
                __result = d.Sd.Illumination > __instance.SightCurve.value.Evaluate(dist / __instance.SightDistanceMax.value);
            }
        }

        [HarmonyPatch(typeof(Humanoid.Agent.EnemyHunter.Tasks.IsOnCamera), "OnCheck")]
        static class OnCam
        {   // "player is looking at the nun": only the remote client can know; it reports it in its flags.
            static void Postfix(ref bool __result)
            {
                var d = Targets.HostMP ? Targets.CurDummy : null;
                if (d != null) __result = d.Flag(PlayerFlags.SeesNun);
            }
        }

        [HarmonyPatch(typeof(GrannyClone.Tasks.PlayerCrouchingCheck), "OnExecute")]
        static class Crouch
        {
            static void Postfix(GrannyClone.Tasks.PlayerCrouchingCheck __instance)
            {
                var d = Targets.HostMP ? Targets.CurDummy : null;
                if (d != null) __instance.AttackStoppingDistanceVar.value = d.Flag(PlayerFlags.Crouch) ? __instance.CrouchStoppingDist : __instance.StandStoppingDist;
            }
        }

        [HarmonyPatch(typeof(GrannyClone.Tasks.PlayerInVentCheck), "OnExecute")]
        static class Vent
        {
            static void Postfix(GrannyClone.Tasks.PlayerInVentCheck __instance)
            {
                var d = Targets.HostMP ? Targets.CurDummy : null;
                if (d != null) __instance.ventStatus.value = d.Flag(PlayerFlags.InVent);
            }
        }

        [HarmonyPatch(typeof(GrannyClone.Tasks.LighterCheck), "OnExecute")]
        static class Lighter
        {
            static void Postfix(GrannyClone.Tasks.LighterCheck __instance)
            {
                var d = Targets.HostMP ? Targets.CurDummy : null;
                if (d == null) return;
                var lighter = d.Flag(PlayerFlags.Lighter);
                __instance.LighterStatus.value = lighter || d.Flag(PlayerFlags.Light);
                __instance.SightLayers.value = lighter ? __instance.onLighter : __instance.offLighter;
            }
        }

        // Remote players under beds: their bed (sent by them) drives the nun's bed flip.
        [HarmonyPatch(typeof(GrannyClone.Tasks.UnderBedHidingCheck), "OnExecute")]
        static class Bed
        {
            static void Postfix(GrannyClone.Tasks.UnderBedHidingCheck __instance)
            {
                var d = Targets.HostMP ? Targets.CurDummy : null;
                if (d == null) return;
                bool under = d.Bed && (d.P.Flags & PlayerFlags.Hiding) != 0;
                __instance.isUnderBed.value = under;
                if (under) { __instance.KillerPt.value = d.Bed.killerPt; __instance.BedPlayerHidingUnder.value = d.Bed.transform; }
            }
        }

        // The host's nun flips a bed: flip it on every screen.
        [HarmonyPatch(typeof(GrannyClone.Tasks.FlipBed), "OnExecute")]
        static class FlipHook
        {
            static void Prefix(GrannyClone.Tasks.FlipBed __instance)
            {
                if (!Targets.HostMP) return;
                var bed = __instance.BedToFlip.value;
                var id = WorldSync.Id(bed ? bed.GetComponent<HideBedInteractable>() : null);
                if (id == null) return;
                float delay = __instance.Delay.value, dur = __instance.Duration.value;
                Plugin.Log.LogInfo("nun flips " + bed.name);
                Session.Broadcast(Msg.Event, w => { w.Write((byte)Ev.FlipBed); w.Write(id); w.Write(delay); w.Write(dur); }, true);
            }
        }

        // Attack-animation damage always hits the LOCAL player. Every peer animates the nun, so each peer
        // applies it to itself only if it is her target and actually within reach (what you see hits you).
        [HarmonyPatch(typeof(DamagePlayer), "Damage")]
        static class Dmg
        {
            static bool Prefix(int amount)
            {
                if (!GameFlow.InGame) return true;
                if (NunMode.Active) return false; // only the nun player's confirmed swing (NEv.Hit) hurts
                bool mine = Session.IsHost ? Targets.Current == Targets.LocalSlot : NunSync.TargetSlot == Session.MySlot;
                var k = NunSync.Killer; var me = PlayerSync.Local;
                bool close = k && me && Vector3.Distance(k.transform.position, me.transform.position) < 4f;
                Plugin.Log.LogInfo($"DamagePlayer({amount}) mine={mine} close={close}");
                return mine && close;
            }
        }
    }
}
