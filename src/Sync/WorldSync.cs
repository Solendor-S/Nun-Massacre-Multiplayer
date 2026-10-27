using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GrannyClone;
using HarmonyLib;
using NodeCanvas;
using NodeCanvas.Framework;
using ParadoxNotion;
using NunMP.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NunMP.Sync
{
    public enum WEv : byte { FsmEvent = 3, StateChange, Light, Glass, ItemTaken, ItemThrown, KillerEvent, Cabinet, Move }

    // Replicates ROOT world actions (player-caused), never their internal follow-ups: every peer runs the same
    // FSMs, so replaying the root reproduces the rest. While replaying, tasks that would touch the LOCAL
    // player (camera grabs, state changes, fades) are skipped.
    public static class WorldSync
    {
        static readonly Dictionary<string, Component> byId = new Dictionary<string, Component>();
        static readonly Dictionary<Component, string> idOf = new Dictionary<Component, string>();
        static bool built;
        static float buildAt;

        public static bool Applying;            // inside a remote replay
        public static int HookDepth, TaskDepth; // nested hooked roots / running FSM tasks
        static readonly Dictionary<GameObject, float> remoteCtx = new Dictionary<GameObject, float>();
        static readonly Stack<GameObject> execOwners = new Stack<GameObject>();
        static System.Action savedClear;

        // Tools everyone needs stay personal: taking/dropping yours doesn't touch anyone else's copy.
        // (Lighters aren't tools any more: there is one per player, see SpawnLighters.)
        static bool IsTool(ItemInteractable it) => it.name.Contains("Radar") || it.name.Contains("Flashlight");

        // FSMs that are per-player presentation / position triggers: never replicated.
        static readonly Regex LocalOnly = new Regex("Killer|CRT|Video_Cameras|Vhs|LoseLife|Death Scene|Intro Outro|Credits|CFV|Snapshot|Enter Building|Can Pass|Player Detect|Warp to|Ending|EndTrigger|Scare|Ghost|Flashlight|Main menu|Bootup");

        public static void Init()
        {
            SceneManager.sceneLoaded += (s, _) => { built = false; byId.Clear(); idOf.Clear(); remoteCtx.Clear(); buildAt = Time.unscaledTime + 2f; };
            Session.On(Msg.Event, (slot, r) =>
            {
                if (!GameFlow.InGame) return;
                var ev = (WEv)r.ReadByte();
                if (ev < WEv.FsmEvent || ev > WEv.Move) return; // other subsystems' events
                Build();
                Applying = true;
                try { Apply(ev, r); }
                catch (System.Exception e) { Plugin.Log.LogError($"world apply {ev}: {e}"); }
                finally { Applying = false; }
            });
        }

        public static void Tick() { if (GameFlow.InGame && !built && Time.unscaledTime > buildAt) Build(); }

        // Path with sibling indices, taken ~2 s after load (after seeded random placement), so it is
        // identical on every peer and stays valid after items get reparented.
        static void Build()
        {
            if (built) return;
            built = true;
            SpawnLighters();
            Radio.Spawn();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                    if (c is GraphOwner || c is StateInteractable || c is ItemInteractable || c is LightSwitchInteractable || c is BreakableGlass || c is CabinetsInteractable || c is MoveInteractable || c is HideBedInteractable)
                    {
                        var id = c.GetType().Name + ":" + PathOf(c.transform);
                        if (byId.ContainsKey(id)) continue; // ponytail: true duplicates (same name+index) can't be told apart
                        byId[id] = c; idOf[c] = id;
                    }
            Plugin.Log.LogInfo($"world ids: {byId.Count}");
        }

        // One lighter per survivor, all in the main lighter's own drawer (so nobody has to hunt). Runs on every
        // peer before IDs are taken, with the same player count, so the clones match everywhere.
        public static ItemInteractable MainLighter() =>
            Object.FindObjectsOfType<ItemInteractable>().FirstOrDefault(i => i.GetComponent<LighterToggleTEMP>() && i.transform.root.name == "Scripted Main Game OFF" && !i.name.Contains("MP"));

        public static int Survivors => DevSurvivors > 0 ? DevSurvivors : Session.Peers.Count - (Session.Mode == NunMode.Id ? 1 : 0);
        // dev: NUNMP_SURVIVORS=8 fakes a full lobby's lighters/radios with 2 lab copies
        static readonly int DevSurvivors = int.TryParse(System.Environment.GetEnvironmentVariable("NUNMP_SURVIVORS"), out var n) ? n : 0;

        static void SpawnLighters()
        {
            int extra = Survivors - 1;
            var orig = MainLighter();
            if (extra <= 0 || !orig) return;
            var drawer = orig.transform.parent;
            for (int i = 0; i < extra; i++)
            {
                var c = Object.Instantiate(orig.gameObject, drawer);
                c.name = "Lighter Item MP" + (i + 2);
                c.transform.position = DrawerSpot(drawer, orig.transform.position, i + 1, 0.2f);
                c.transform.rotation = orig.transform.rotation;
            }
            Plugin.Log.LogInfo($"spawned {extra} extra lighter(s) in {drawer.name}");
        }

        // Spot #k beside `start`: a grid `step` apart inside the drawer, nearest spots first along the long side,
        // then further rows across the depth once a row is full (8 players don't fit in one row).
        public static Vector3 DrawerSpot(Transform drawer, Vector3 start, int k, float step)
        {
            var col = drawer.GetComponent<Collider>();
            if (!col) return start + Vector3.up * 0.02f * k;
            var b = col.bounds;
            bool xLong = b.size.x >= b.size.z;
            Vector3 along = xLong ? Vector3.right : Vector3.forward, across = xLong ? Vector3.forward : Vector3.right;
            float half = Mathf.Max(0.05f, (xLong ? b.size.x : b.size.z) / 2f - 0.12f);
            float halfD = Mathf.Max(0f, (xLong ? b.size.z : b.size.x) / 2f - 0.08f);
            float c = Vector3.Dot(b.center, along), s0 = Vector3.Dot(start, along);
            float cd = Vector3.Dot(b.center, across), d0 = Vector3.Dot(start, across);
            // offsets from the start that stay inside, nearest first (0, +-step, +-2step...; roomier side first)
            float dir = s0 <= c ? 1 : -1;
            var row = new System.Collections.Generic.List<float> { 0 };
            for (int j = 1; j < 20; j++)
                foreach (var o in new[] { j * step * dir, -j * step * dir })
                    if (Mathf.Abs(s0 + o - c) <= half) row.Add(o);
            int r = k / row.Count;
            // ponytail: shallow drawer -> extra rows clamp onto the edge and may touch; fine up to 8
            float od = Mathf.Clamp(d0 + step * ((r + 1) / 2) * (r % 2 == 1 ? 1 : -1), cd - halfD, cd + halfD) - d0;
            return start + along * row[k % row.Count] + across * od;
        }

        static string PathOf(Transform t)
        {
            var sb = new StringBuilder();
            for (; t; t = t.parent) sb.Insert(0, "/" + t.GetSiblingIndex() + t.name);
            return sb.ToString();
        }

        public static string Id(Component c) { Build(); return c && idOf.TryGetValue(c, out var id) ? id : null; }
        public static T Find<T>(string id) where T : Component { Build(); return Get<T>(id); }
        static T Get<T>(string id) where T : Component => byId.TryGetValue(id, out var c) ? c as T : null;

        public static bool CanSend => GameFlow.InGame && !Applying;
        // Scene setup fires StateChange/SendEvent identically everywhere; nothing before this is a player action.
        static bool Settled => built && Time.unscaledTime > buildAt + 8f;

        public static void Send(WEv ev, System.Action<System.IO.BinaryWriter> body) =>
            Session.Broadcast(Msg.Event, w => { w.Write((byte)ev); body(w); }, true);

        static void Apply(WEv ev, System.IO.BinaryReader r)
        {
            var id = r.ReadString();
            switch (ev)
            {
                case WEv.FsmEvent:
                    {
                        var name = r.ReadString();
                        var o = Get<GraphOwner>(id);
                        if (o) { Mark(o.gameObject); o.SendEvent(name); }
                        Plugin.Log.LogInfo($"remote fsm event {name} -> {id} {(o ? "" : "(missing)")}");
                        break;
                    }
                case WEv.StateChange:
                    {
                        var idx = r.ReadInt32();
                        var usedId = r.ReadString();
                        var si = Get<StateInteractable>(id);
                        if (si)
                        {   // puzzles that consume/place an item (plank over barbed wire, keys) need OUR copy of it
                            var used = usedId.Length > 0 ? Get<ItemInteractable>(usedId) : null;
                            if (used)
                            {   // as if held: kinematic + no collider (Interact does this for the holder), else it falls while being placed
                                used.gameObject.SetActive(true);
                                var urb = used.GetComponent<Rigidbody>(); if (urb) urb.isKinematic = true;
                                var ucol = used.GetComponent<Collider>(); if (ucol) ucol.enabled = false;
                                si.ItemUsed = used;
                            }
                            Mark(si.gameObject); si.StateChange(idx);
                        }
                        Plugin.Log.LogInfo($"remote state {idx} -> {id} {(si ? "" : "(missing)")}");
                        break;
                    }
                case WEv.Light: Get<LightSwitchInteractable>(id)?.Interact(); break;
                case WEv.Glass: Get<BreakableGlass>(id)?.BreakGlass(); break;
                case WEv.Cabinet: Get<CabinetsInteractable>(id)?.Interact(); break;
                case WEv.Move:
                    {
                        var m = Get<MoveInteractable>(id);
                        var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        var rot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        if (m) DoorSync.Remote(m, pos, rot);
                        break;
                    }
                case WEv.ItemTaken:
                    {
                        var it = Get<ItemInteractable>(id);
                        if (it) it.gameObject.SetActive(false);
                        break;
                    }
                case WEv.ItemThrown:
                    {
                        var it = Get<ItemInteractable>(id);
                        var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        var rot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        var imp = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        if (!it) break;
                        it.gameObject.SetActive(true);
                        it.transform.SetParent(null, true);
                        it.transform.SetPositionAndRotation(pos, rot);
                        it.ResetToPickupable();
                        var col = it.GetComponent<Collider>(); if (col) col.isTrigger = false;
                        var rb = it.GetComponent<Rigidbody>(); if (rb) { rb.isKinematic = false; rb.velocity = Vector3.zero; rb.AddForce(imp, ForceMode.Impulse); }
                        break;
                    }
                case WEv.KillerEvent:
                    Plugin.Log.LogInfo("client poked the nun: " + id);
                    // authoritative on the host: not a replay, so no remote-context marking
                    Applying = false;
                    if (Session.IsHost && NunSync.Killer) NunSync.Killer.GetComponent<GraphOwner>()?.SendEvent(id);
                    break;
            }
        }

        static void Mark(GameObject go) => remoteCtx[go] = Time.time + 8f;
        static bool InRemoteCtx(GameObject go) => go && remoteCtx.TryGetValue(go, out var until) && Time.time < until;

        // ---------------- hooks ----------------

        [HarmonyPatch(typeof(Graph), nameof(Graph.SendEvent), typeof(EventData))]
        static class SendEventHook
        {
            static void Prefix(Graph __instance, EventData eventData)
            {
                if (!GameFlow.InGame || eventData == null || !(__instance.agent is Component ag) || !ag) return;
                var go = ag.gameObject;
                var src = execOwners.Count > 0 ? execOwners.Peek() : null;
                bool remote = Applying || InRemoteCtx(src);
                if (remote) { Mark(go); return; } // consequence of a replay: stays local
                if (go == NunSync.Killer)
                {
                    if (NunMode.Active) return; // no AI to poke   // client-side triggers that poke the nun (enter building, alarms...) belong to the host's nun
                    if (!Session.IsHost) Send(WEv.KillerEvent, w => w.Write(eventData.name));
                    return;
                }
                if (!Settled || TaskDepth > 0 || HookDepth > 0 || LocalOnly.IsMatch(PathOf(go.transform))) return; // setup / internal follow-up / local presentation
                var id = Id(__instance.agent as GraphOwner ?? go.GetComponent<GraphOwner>());
                if (id == null) return;
                Plugin.Log.LogInfo($"send fsm event {eventData.name} -> {id}");
                Send(WEv.FsmEvent, w => { w.Write(id); w.Write(eventData.name); });
            }
        }

        // Track which graph's task is running, and skip player-touching tasks inside replays.
        [HarmonyPatch(typeof(ActionTask), nameof(ActionTask.ExecuteAction), typeof(Component), typeof(IBlackboard))]
        static class TaskHook
        {
            static readonly Regex PlayerTasks = new Regex(@"PlayerStateChange|CameraMoveToPt|PickupItem|PlayerInteractionUiToggle|\.Fade|FadeIn|FadeOut|CameraFader|TweenFov");

            static bool Prefix(ActionTask __instance, ref Status __result, out bool __state)
            {
                __state = false;
                if (!GameFlow.InGame) return true;
                var owner = __instance.ownerSystem?.agent ? __instance.ownerSystem.agent.gameObject : null;
                // Barbed-wire pit = vanilla "bad ending". In co-op it's just a death (costs a life, respawn).
                if (owner && owner.name == "Ending trigger" && owner.transform.parent && owner.transform.parent.name.StartsWith("Barbed Wire Puzzle"))
                {
                    owner.GetComponent<GraphOwner>()?.StopBehaviour();
                    if (PlayerSync.Local && PlayerSync.Local.health && PlayerSync.Local.health.isAlive()) PlayerSync.Local.health.Damage(500f);
                    Plugin.Log.LogInfo("barbed wire pit -> normal death (co-op)");
                    __result = Status.Success;
                    return false;
                }
                // Every respawn sends the nun "NunReset" -> Retreat warps her to a retreat point. Solo that hides her
                // from the respawned player; in co-op the others watch her vanish. Skip the warp: she runs there instead.
                if (Session.Active && !NunMode.Active && __instance.GetType().Name == "WarpTo" && RetreatWarp(__instance))
                {
                    Plugin.Log.LogInfo("nun retreat: running instead of warping (co-op)");
                    __result = Status.Success;
                    return false;
                }
                if (InRemoteCtx(owner) && __instance is GrannyClone.Tasks.PlaceItemTask && savedClear == null)
                {   // place OUR copy of the item, but don't empty our own hands
                    savedClear = MessagesGranny.ClearHoldingItemPlayerEvent ?? (() => { });
                    MessagesGranny.ClearHoldingItemPlayerEvent = null;
                }
                if (InRemoteCtx(owner) && __instance.status != Status.Running)
                {
                    Component ag = null;
                    try { ag = __instance.agent; } catch { }
                    var me = PlayerSync.Local ? PlayerSync.Local.transform : null;
                    bool touchesMe = ag && me && Mine(ag, me) || ParamsTouch(__instance, me);
                    if (touchesMe || PlayerTasks.IsMatch(__instance.GetType().FullName))
                    {
                        __result = Status.Success;
                        return false;
                    }
                }
                execOwners.Push(owner); TaskDepth++; __state = true;
                return true;
            }

            static bool RetreatWarp(ActionTask t) =>
                t.GetType().GetField("Target")?.GetValue(t) is BBParameter bp && bp.name == "CurrentRetreat";

            static bool Mine(Component c, Transform me) => c.transform.IsChildOf(me) || (Camera.main && c.transform.IsChildOf(Camera.main.transform));

            // Warps/tweens often name the player in a BB parameter rather than as the agent.
            static bool ParamsTouch(ActionTask t, Transform me)
            {
                if (!me) return false;
                foreach (var f in t.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    if (typeof(BBParameter).IsAssignableFrom(f.FieldType) && f.GetValue(t) is BBParameter bp)
                    {
                        object v; try { v = bp.value; } catch { continue; }
                        if (v is Component c && c && Mine(c, me)) return true;
                        if (v is GameObject g && g && Mine(g.transform, me)) return true;
                    }
                return false;
            }

            static void Postfix(bool __state) { if (__state) { execOwners.Pop(); TaskDepth--; } Restore(); }
            static System.Exception Finalizer(System.Exception __exception, bool __state) { if (__state && __exception != null) { execOwners.Pop(); TaskDepth--; } Restore(); return __exception; }
            static void Restore() { if (savedClear != null) { MessagesGranny.ClearHoldingItemPlayerEvent = savedClear; savedClear = null; } }
        }

        // StateChange also runs at scene setup on every peer; only interaction-driven ones are roots.
        static int interacting;
        [HarmonyPatch(typeof(StateInteractable), nameof(StateInteractable.Interact))]
        static class InteractGate
        {
            static void Prefix() => interacting++;
            static void Finalizer() => interacting--;
        }

        [HarmonyPatch(typeof(StateInteractable), nameof(StateInteractable.StateChange))]
        static class StateHook
        {
            static void Prefix(StateInteractable __instance, int index)
            {
                HookDepth++;
                if (!CanSend || (interacting == 0 && !Settled) || TaskDepth > 0 || HookDepth > 1 || index < 0 || InRemoteCtx(__instance.gameObject)) return;
                if (LocalOnly.IsMatch(PathOf(__instance.transform))) return; // e.g. crawling through the vent fan is mine alone
                var id = Id(__instance);
                if (id == null) return;
                Plugin.Log.LogInfo($"send state {index} -> {id}");
                var usedId = __instance.ItemUsed ? Id(__instance.ItemUsed) ?? "" : "";
                Send(WEv.StateChange, w => { w.Write(id); w.Write(index); w.Write(usedId); });
            }
            static void Postfix() => HookDepth--;
        }

        [HarmonyPatch(typeof(LightSwitchInteractable), nameof(LightSwitchInteractable.Interact))]
        static class LightHook
        {
            static void Prefix(LightSwitchInteractable __instance)
            {
                HookDepth++;
                if (CanSend && HookDepth == 1 && Id(__instance) is string id) Send(WEv.Light, w => w.Write(id));
            }
            static void Postfix() => HookDepth--;
        }

        [HarmonyPatch(typeof(CabinetsInteractable), nameof(CabinetsInteractable.Interact))]
        static class CabinetHook
        {
            static void Prefix(CabinetsInteractable __instance)
            {
                HookDepth++;
                if (CanSend && HookDepth == 1 && Id(__instance) is string id) Send(WEv.Cabinet, w => w.Write(id));
            }
            static void Postfix() => HookDepth--;
        }

        [HarmonyPatch(typeof(MoveInteractable), nameof(MoveInteractable.Interact))]
        static class DragHook { static void Postfix(MoveInteractable __instance) { if (CanSend) DoorSync.LocalDrag(__instance); } }

        // PlaceItem (UnityEvent-driven twin of PlaceItemTask): same "don't empty my hands" rule during replays.
        [HarmonyPatch(typeof(PlaceItem), nameof(PlaceItem.Place))]
        static class PlaceHook
        {
            static void Prefix(PlaceItem __instance)
            {
                if ((Applying || InRemoteCtx(__instance.gameObject)) && savedClear == null)
                { savedClear = MessagesGranny.ClearHoldingItemPlayerEvent ?? (() => { }); MessagesGranny.ClearHoldingItemPlayerEvent = null; }
            }
            static void Finalizer() { if (savedClear != null) { MessagesGranny.ClearHoldingItemPlayerEvent = savedClear; savedClear = null; } }
        }

        [HarmonyPatch(typeof(BreakableGlass), nameof(BreakableGlass.BreakGlass))]
        static class GlassHook
        {
            static void Prefix(BreakableGlass __instance)
            {
                HookDepth++;
                if (CanSend && HookDepth == 1 && Id(__instance) is string id) Send(WEv.Glass, w => w.Write(id));
            }
            static void Postfix() => HookDepth--;
        }

        [HarmonyPatch(typeof(ItemInteractable), nameof(ItemInteractable.Interact))]
        static class TakeHook
        {
            static void Prefix(ItemInteractable __instance, out bool __state) { __state = !__instance.CantPickUp && !IsTool(__instance); HookDepth++; }
            static void Postfix(ItemInteractable __instance, bool __state)
            {
                HookDepth--;
                if (__state && CanSend && Id(__instance) is string id) Send(WEv.ItemTaken, w => w.Write(id));
            }
        }

        [HarmonyPatch(typeof(ItemInteractable), nameof(ItemInteractable.Throw))]
        static class ThrowHook
        {
            static void Prefix(ItemInteractable __instance, Vector3 direction)
            {
                if (!CanSend || IsTool(__instance) || !(Id(__instance) is string id)) return;
                var t = __instance.transform;
                var imp = direction * __instance.throwForce;
                Send(WEv.ItemThrown, w =>
                {
                    w.Write(id);
                    w.Write(t.position.x); w.Write(t.position.y); w.Write(t.position.z);
                    w.Write(t.rotation.x); w.Write(t.rotation.y); w.Write(t.rotation.z); w.Write(t.rotation.w);
                    w.Write(imp.x); w.Write(imp.y); w.Write(imp.z);
                });
            }
        }
    }
}
