using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using HarmonyLib;
using NunMP.Net;
using UnityEngine;

namespace NunMP.Sync
{
    public enum NEv : byte { Hit = 40, Trap, Pull, Gas, Scream, Lights, Noise, Scared, Shot }

    // "Play as Nun": one player drives the nun (NunPlayer.cs), the AI is off everywhere. This class holds the
    // mode switches the other systems check, and everything survivors experience (hits, pulls, powers, chase music).
    public class NunMode : MonoBehaviour
    {
        public const string Id = "nunplayer";
        public static bool Active => GameFlow.InGame && Session.Mode == Id;
        public static bool IAmNun => Active && Session.MySlot == Rules.NunSlot;
        public static bool Sneaking, Sprinting;           // nun state (from her snapshot)
        static readonly List<(Vector3 p, float t)> pings = new List<(Vector3, float)>();
        public static IEnumerable<Vector3> RecentPings => pings.Where(x => Time.time - x.t < 3f).Select(x => x.p);
        static readonly List<(Light l, float until)> darkened = new List<(Light, float)>();
        static float flickerUntil, staticUntil, lastSeenNun = -99, chaseOn;
        static bool chasing;
        public static string SeeDebug = "";

        public static void Init()
        {
            Session.On(Msg.Event, (slot, r) =>
            {
                if (!GameFlow.InGame) return;
                var ev = (NEv)r.ReadByte();
                if (ev == NEv.Shot) { Shot(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle())); return; }
                if (!Active) return;
                switch (ev)
                {
                    case NEv.Hit:
                        {
                            var target = r.ReadByte(); var dmg = r.ReadSingle();
                            if (target == Session.MySlot && !IAmNun && PlayerSync.Local && PlayerSync.Local.health) PlayerSync.Local.health.Damage(dmg);
                            break;
                        }
                    case NEv.Trap: SpawnTrap(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), r.ReadSingle()); break;
                    case NEv.Pull:
                        if (r.ReadByte() == Session.MySlot && PlayerSync.Local && PlayerSync.Local.playerStates.StateCheck() == PlayerStates.States.Hiding)
                        {
                            Plugin.Log.LogInfo("the nun dragged me out");
                            MEC.Timing.RunCoroutine(PlayerSync.Local.playerStates.exitHiding());
                        }
                        break;
                    case NEv.Gas: MessagesGranny.PoisonGasSetEvent?.Invoke(r.ReadBoolean()); break;
                    case NEv.Scream: Scream(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle())); break;
                    case NEv.Lights: LightsOut(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), r.ReadSingle(), r.ReadSingle()); break;
                    case NEv.Noise: if (IAmNun) pings.Add((new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), Time.time)); break;
                    case NEv.Scared: NunPlayer.Scared(r.ReadByte()); break;
                }
            });
            // Revolver: the nun only reacts where her AI runs (co-op host) or where she's played (nun mode).
            // Everywhere else her copy is a puppet with the AI off, so pass the hit on.
            MessagesGranny.GunDamageEvent += (col, _, pt) =>
            {
                var k = NunSync.Killer;
                if (relaying || !GameFlow.InGame || !Session.Active || !k || !col || !col.transform.IsChildOf(k.transform)) return;
                if (!Active && Session.IsHost) return; // the host's own AI already heard it
                Plugin.Log.LogInfo("I shot the nun");
                Session.Broadcast(Msg.Event, w => { w.Write((byte)NEv.Shot); w.Write(pt.x); w.Write(pt.y); w.Write(pt.z); }, true);
            };
            // Survivor noise (steps, doors, talking...) goes to the nun player, who sees it as a ping.
            Message.Detectable.NoiseMadeEvent += (pos, rad) =>
            {
                if (!Active || IAmNun || rad < 2f) return;
                Send(NEv.Noise, w => { w.Write(pos.x); w.Write(pos.y); w.Write(pos.z); });
            };
        }

        public static void Send(NEv ev, System.Action<System.IO.BinaryWriter> body)
        {
            Session.Broadcast(Msg.Event, w => { w.Write((byte)ev); body(w); }, true);
            // host/broadcaster doesn't receive its own message: apply locally too
            var ms = new System.IO.MemoryStream(); var bw = new System.IO.BinaryWriter(ms); bw.Write((byte)ev); body(bw);
            ApplyLocal(ms.ToArray());
        }

        static void ApplyLocal(byte[] payload)
        {
            var r = new System.IO.BinaryReader(new System.IO.MemoryStream(payload));
            var ev = (NEv)r.ReadByte();
            switch (ev)
            {
                case NEv.Trap: SpawnTrap(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), r.ReadSingle()); break;
                case NEv.Gas: MessagesGranny.PoisonGasSetEvent?.Invoke(r.ReadBoolean()); break;
                case NEv.Scream: Scream(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle())); break;
                case NEv.Lights: LightsOut(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), r.ReadSingle(), r.ReadSingle()); break;
            }
        }

        static bool relaying;
        static void Shot(Vector3 pt)
        {
            if (Active) { if (IAmNun) NunPlayer.Shot(); return; }
            var k = NunSync.Killer;
            if (!Session.IsHost || !k) return;
            // co-op host: replay the shot into her AI (its ReceivedDamage listens for a hit on her own collider)
            var col = k.GetComponents<Collider>().FirstOrDefault(c => !c.isTrigger) ?? k.GetComponent<Collider>();
            if (!col) { Plugin.Log.LogWarning("nun has no collider to shoot"); return; }
            Plugin.Log.LogInfo("a client shot the nun");
            relaying = true;
            try { MessagesGranny.GunDamageEvent?.Invoke(col, 0f, pt); } finally { relaying = false; }
        }

        // ---- powers, as experienced on every machine ----

        static void SpawnTrap(Vector3 pos, float yaw)
        {
            // the AI nun's trap prefab is referenced by her FSM (not placed in the scene)
            var refs = NunSync.Killer ? NunSync.Killer.GetComponent<NodeCanvas.Framework.GraphOwner>()?.graph?._objectReferences : null;
            var tpl = refs?.OfType<Component>().FirstOrDefault(c => c && c.name == "Bear Trap")?.gameObject
                ?? Resources.FindObjectsOfTypeAll<BearTrap>().FirstOrDefault()?.gameObject;
            if (!tpl) { Plugin.Log.LogWarning("no bear trap template"); return; }
            var t = Instantiate(tpl, pos, Quaternion.Euler(0, yaw, 0));
            t.name = "NunMP Trap";
            t.SetActive(true);
        }

        static void Scream(Vector3 at)
        {
            var src = GameObject.Find("Characters/Killer Stuff new/Audio Follow Killer/Chase/Scream");
            var rnd = src ? src.GetComponent<AudioSourceRandom>() : null;
            if (rnd) rnd.PlayRandom(); else src?.GetComponent<AudioSource>()?.Play();
            var me = PlayerSync.Local;
            if (IAmNun || !me || Vector3.Distance(me.transform.position, at) > 12f) return;
            staticUntil = Time.time + 3f;                              // screen tears
            var st = me.GetComponent<pStamina>();
            if (st) st.ChangeStamina(st.Meter * 0.3f);                 // and your legs go weak
        }

        static void LightsOut(Vector3 at, float radius, float secs)
        {
            var me = PlayerSync.Local ? PlayerSync.Local.transform : null;
            foreach (var l in FindObjectsOfType<Light>())
            {
                if (!l.enabled || (me && l.transform.IsChildOf(me)) || l.GetComponentInParent<Puppet>()) continue;
                if (Vector3.Distance(l.transform.position, at) > radius) continue;
                l.enabled = false; darkened.Add((l, Time.time + secs));
            }
            if (me && Vector3.Distance(me.position, at) < radius) flickerUntil = Time.time + secs;
        }

        void Update()
        {
            if (!Active) { if (darkened.Count > 0) Restore(true); return; }
            Restore(false);
            FlickerMyLight();
            if (!IAmNun) ChaseMusic();
        }

        static void Restore(bool all)
        {
            for (int i = darkened.Count - 1; i >= 0; i--)
                if (all || Time.time > darkened[i].until) { if (darkened[i].l) darkened[i].l.enabled = true; darkened.RemoveAt(i); }
        }

        static readonly Dictionary<Light, float> baseIntensity = new Dictionary<Light, float>();
        static void FlickerMyLight()
        {
            var me = PlayerSync.Local;
            if (!me || IAmNun) return;
            if (Time.time < flickerUntil)
                foreach (var l in me.GetComponentsInChildren<Light>())
                {
                    if (!baseIntensity.ContainsKey(l)) baseIntensity[l] = l.intensity;
                    // mostly a dying ember, with dropouts to black and the odd weak sputter
                    float f = Mathf.PerlinNoise(Time.time * 9f, l.GetInstanceID()) * 0.3f;
                    if (Random.value < 0.18f) f = 0f; else if (Random.value < 0.05f) f = 0.6f;
                    l.intensity = baseIntensity[l] * f;
                }
            else if (baseIntensity.Count > 0)
            {
                foreach (var kv in baseIntensity) if (kv.Key) kv.Key.intensity = kv.Value;
                baseIntensity.Clear();
            }
        }

        // Survivors' own chase music + jumpscare sting: she's close and you can see her.
        static void ChaseMusic()
        {
            var k = NunSync.Killer; var me = PlayerSync.Local;
            if (!k || !me || !me.camera) return;
            var eye = me.camera.transform.position; var chest = k.transform.position + Vector3.up * 1.4f;
            bool blocked = Physics.Linecast(eye, chest, out var hit, ~(1 << LayerMask.NameToLayer("Player")), QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(k.transform);
            // she has to actually be on my screen (not just near and unobstructed)
            var vp = me.camera.WorldToViewportPoint(chest);
            bool onScreen = vp.z > 0 && vp.x > 0.05f && vp.x < 0.95f && vp.y > 0.05f && vp.y < 0.95f;
            bool sees = Vector3.Distance(eye, chest) < 18f && !blocked && onScreen;
            SeeDebug = $"sees={sees} onScreen={onScreen} d={Vector3.Distance(eye, chest):0.0} hit={(blocked ? hit.collider.name : "-")} chasing={chasing}";
            var root = GameObject.Find("Characters/Killer Stuff new/Audio Follow Killer/Chase");
            if (!root) return;
            var bell = root.transform.Find("Bell Loop")?.GetComponent<AudioSource>();
            var sting = root.transform.Find("Stinger")?.GetComponent<AudioSource>();
            if (sees)
            {
                if (!chasing && Time.time - lastSeenNun > 20f && sting)
                {
                    sting.mute = false; sting.volume = 1f; sting.Play();
                    var me2 = Session.MySlot; Send(NEv.Scared, w => w.Write(me2)); // tell the nun she got me
                }
                chasing = true; lastSeenNun = Time.time;
            }
            else if (chasing && Time.time - lastSeenNun > 6f) chasing = false;
            if (bell)
            {
                bell.mute = false;
                if (chasing && !bell.isPlaying) bell.Play();
                bell.volume = Mathf.MoveTowards(bell.volume, chasing ? 1f : 0f, Time.deltaTime * 0.7f);
                if (!chasing && bell.volume <= 0.01f && bell.isPlaying) bell.Stop();
            }
        }

        // Screen tear near her comes from the VHS camera FSM's distance check ("Tape Damage"): off while she
        // sneaks, forced on for a few seconds by her scream.
        static GameObject tape, tapeChase;
        void LateUpdate()
        {
            if (!Active || IAmNun) return;
            if (!tape) { tape = Find("Global/Video_Cameras/Vhs Damage Cam Fsm/Tape Damage"); tapeChase = Find("Global/Video_Cameras/Vhs Damage Cam Fsm/Tape Damage Chase"); }
            if (Time.time < staticUntil) { if (tape) tape.SetActive(true); overridden = true; }
            else if (Sneaking) { if (tape) tape.SetActive(false); if (tapeChase) tapeChase.SetActive(false); overridden = true; }
            else if (overridden && tape)
            {   // hand back to the VHS FSM: it only toggles when its 'tapeDamage' flag changes, so match the flag
                overridden = false;
                var own = tape.transform.parent ? tape.transform.parent.GetComponent<NodeCanvas.Framework.GraphOwner>() : null; // (tape is inactive here)
                var v = own ? (own.blackboard?.GetVariable<bool>("tapeDamage") ?? own.graph?.blackboard?.GetVariable<bool>("tapeDamage")) : null;
                tape.SetActive(v != null && v.value);
            }
        }
        static bool overridden;
        static GameObject Find(string path) => Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.gameObject.scene.IsValid() && Plugin.Path_(t) == path)?.gameObject;

        // In nun mode damage only comes from the nun player's confirmed swing (NEv.Hit): mute the puppet's own.
        [HarmonyPatch(typeof(MeleeWeapon), nameof(MeleeWeapon.CauseDamageToggle))]
        static class MeleeHook { static bool Prefix() => !Active; }
    }
}
