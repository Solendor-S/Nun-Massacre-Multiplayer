using System.Linq;
using GrannyClone;
using HarmonyLib;
using NodeCanvas.Framework;
using NunMP.Net;
using UnityEngine;

namespace NunMP.Sync
{
    public enum LEv : byte { Lives = 20, LifeLost, GameOver, Escaped, Died }

    // Shared lives pool (host-owned). Caught with lives left = vanilla death scene + respawn.
    // Caught with none left = spectate a teammate. Everyone out = game over for all. One escapes = ending for all.
    public class LivesSync : MonoBehaviour
    {
        public static int Pool = -1;          // -1 = not set yet this round
        public static bool Spectating;
        static bool roundInit;
        static int watch;                     // index into living puppets


        public static void Init()
        {
            Session.On(Msg.Event, (slot, r) =>
            {
                if (!GameFlow.InGame) return;
                var ev = (LEv)r.ReadByte();
                switch (ev)
                {
                    case LEv.Lives: Pool = r.ReadInt32(); ApplyPool(); break;
                    case LEv.LifeLost:
                        if (!Session.IsHost) break;
                        Pool = Mathf.Max(0, Pool - 1);
                        BroadcastPool();
                        break;
                    case LEv.GameOver: Plugin.Log.LogInfo("game over (everyone caught)"); Banner(NunMode.IAmNun ? "THE NUN WINS" : "SHE GOT EVERYONE"); EndLocal(); break;
                    case LEv.Died:
                        if (PlayerSync.Puppets.TryGetValue(slot, out var dead) && dead) dead.PlayDeath();
                        Banner((Session.Peers.TryGetValue(slot, out var dp) ? dp.Name : "Someone") + " WAS CAUGHT", 3f);
                        break;
                    case LEv.Escaped: Plugin.Log.LogInfo("a teammate escaped -> outro"); if (NunMode.Active) Banner(NunMode.IAmNun ? "THEY ESCAPED" : "SURVIVORS ESCAPED"); PlayOutro(); break;
                }
            });
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, _) => { Pool = -1; Spectating = false; roundInit = false; };
        }

        static void BroadcastPool()
        {
            ApplyPool();
            Session.Broadcast(Msg.Event, w => { w.Write((byte)LEv.Lives); w.Write(Pool); }, true);
        }

        static void ApplyPool()
        {
            var ls = LivesSystem.instance;
            if (ls && Pool >= 0 && !Spectating) ls.currentLives = Mathf.Max(Pool, 1); // never let vanilla see 0 while alive
        }

        void Update()
        {
            if (!GameFlow.InGame) return;
            var ls = LivesSystem.instance;
            if (ls && !roundInit)
            {
                roundInit = true;
                int start = Rules.Lives > 0 ? Rules.Lives : ls.totalLives;
                if (Rules.Mode == Rules.LivesMode.PerPlayer) ls.currentLives = start; // vanilla per-player lives, host's count
                else if (Session.IsHost) { Pool = start; BroadcastPool(); }
            }
            if (Spectating && Input.GetKeyDown(KeyCode.Mouse0)) watch++;
        }

        // A teammate is still playing?
        static bool AnyTeammateAlive() => PlayerSync.Puppets.Values.Any(p => p && (p.Flags & PlayerFlags.Dead) == 0);

        void LateUpdate()
        {
            if (!Spectating || !GameFlow.InGame) return;
            var alive = PlayerSync.Puppets.Values.Where(p => p && (p.Flags & PlayerFlags.Dead) == 0).OrderBy(p => p.Slot).ToList();
            var cam = Camera.main;
            if (alive.Count == 0 || !cam) return;
            var p0 = alive[watch % alive.Count];
            var head = p0.transform.position + Vector3.up * 1.6f;
            var back = head - p0.transform.forward * 2.2f + Vector3.up * 0.4f;
            if (Physics.Linecast(head, back, out var hit, ~(1 << LayerMask.NameToLayer("Player")), QueryTriggerInteraction.Ignore)) back = hit.point + (head - back).normalized * 0.2f;
            cam.transform.SetPositionAndRotation(back, Quaternion.LookRotation(head - back));
        }

        static string banner = ""; static float bannerUntil;
        static void Banner(string s, float secs = 8f) { banner = s; bannerUntil = Time.unscaledTime + secs; }

        void OnGUI()
        {
            if (Time.unscaledTime < bannerUntil) GUI.Label(new Rect(Screen.width / 2 - 100, Screen.height * 0.4f, 300, 40), banner);
            if (!Spectating) return;
            GUI.Label(new Rect(Screen.width / 2 - 200, Screen.height - 60, 400, 40), "YOU WERE CAUGHT - spectating (click to switch)");
        }

        static void StartSpectating()
        {
            Spectating = true;
            var me = PlayerSync.Local;
            if (me && me.playerStates) me.playerStates.ChangeState(PlayerStates.States.Cutscene);
            Plugin.Log.LogInfo("out of lives -> spectating");
        }

        static void EndLocal()
        {
            Spectating = false;
            var h = PlayerSync.Local ? PlayerSync.Local.health : null;
            if (h) h.GameOver();
        }

        static void PlayOutro()
        {
            var io = Object.FindObjectOfType<IntroOutroController>();
            if (!io) return;
            var me = PlayerSync.Local;
            if (me && me.playerStates) me.playerStates.ChangeState(PlayerStates.States.Cutscene);
            playingRemoteOutro = true;
            try { io.PlayOutro(); } finally { playingRemoteOutro = false; }
        }

        // ---- vanilla hooks ----

        [HarmonyPatch(typeof(LivesSystem), nameof(LivesSystem.ChangeLives))]
        static class LifeHook
        {
            static void Prefix(LivesSystem __instance, int amount)
            {
                if (GameFlow.InGame && amount < 0) Session.Broadcast(Msg.Event, w => w.Write((byte)LEv.Died), true); // everyone sees me go down
                if (!GameFlow.InGame || amount >= 0 || Rules.Mode != Rules.LivesMode.Shared) return;
                if (Pool >= 0) __instance.currentLives = Pool; // spend from the shared pool
                if (Session.IsHost) { Pool = Mathf.Max(0, Pool - 1); Session.Broadcast(Msg.Event, w => { w.Write((byte)LEv.Lives); w.Write(Pool); }, true); }
                else { Pool = Mathf.Max(0, Pool - 1); Session.Broadcast(Msg.Event, w => w.Write((byte)LEv.LifeLost), true); }
                Plugin.Log.LogInfo($"life lost, pool now {Pool}");
            }
        }

        [HarmonyPatch(typeof(LivesSystem), "CheckIfDead")]
        static class DeadHook
        {
            static bool Prefix(LivesSystem __instance)
            {
                if (!GameFlow.InGame || __instance.currentLives > 0) return true;
                if (Rules.Out == Rules.OutMode.Spectate && AnyTeammateAlive()) { StartSpectating(); return false; }
                // last one standing just fell: everyone's game ends
                Session.Broadcast(Msg.Event, w => w.Write((byte)LEv.GameOver), true);
                return true;
            }
        }

        // Someone reached a real ending (the game's outro scroll): everyone gets it.
        static bool playingRemoteOutro;
        [HarmonyPatch(typeof(IntroOutroController), nameof(IntroOutroController.StartScroll))]
        static class OutroHook
        {
            static void Prefix(bool isIntro)
            {
                if (isIntro || !GameFlow.InGame || playingRemoteOutro) return;
                Plugin.Log.LogInfo("I escaped -> outro for everyone");
                Session.Broadcast(Msg.Event, w => w.Write((byte)LEv.Escaped), true);
            }
        }
    }
}
