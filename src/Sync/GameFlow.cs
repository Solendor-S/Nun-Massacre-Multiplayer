using System.Collections;
using System.Linq;
using HarmonyLib;
using NunMP.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NunMP.Sync
{
    // Host "Start": everyone copies the host's settings + seed, then walks the vanilla menu path (Play -> Skip ahead).
    public static class GameFlow
    {
        public const string GameScene = "Nun scene";
        public static int Seed;
        public static bool InGame => Session.Active && Session.GameStarted && SceneManager.GetActiveScene().name == GameScene;

        public static void Init()
        {
            Session.On(Msg.Pref, (slot, r) =>
            {   // the nun player picked her look (host stores it; everyone sees it in the roster)
                var skin = r.ReadByte();
                if (Session.IsHost && slot == Rules.NunSlot && !Session.GameStarted) { Rules.NunSkin = skin; Session.SendRoster(); }
            });
            Session.On(Msg.Start, (slot, r) =>
            {
                if (slot != 0) return;
                Seed = r.ReadInt32();
                DifficultyController.CurrentDifficulty = (DifficultyLevel)r.ReadInt32();
                RandomizeItemLocationsMode.RandomModeEnabled = r.ReadBoolean();
                TextureSwapper.CurrentID = (TextureSwapID)r.ReadInt32();
                SkinSwapper.CurrentID = (SkinID)r.ReadInt32();
                PlayerPrefs.SetInt("timesPlayed", r.ReadInt32());
                Session.Mode = r.ReadString();
                Rules.Read(r);
                Begin();
            });
            SceneManager.sceneLoaded += (s, _) =>
            {
                if (s.name == "Nun Bootup" && Session.GameStarted) { Session.GameStarted = false; Plugin.Log.LogInfo("back at menu"); }
            };
        }

        public static void HostStart()
        {
            Seed = Random.Range(1, int.MaxValue);
            if (Session.Mode == NunMode.Id && (Rules.NunSlot == 255 || !Session.Peers.ContainsKey(Rules.NunSlot)))
                Rules.NunSlot = Session.Peers.Keys.ElementAt(Random.Range(0, Session.Peers.Count));
            if (Session.Mode == NunMode.Id && Rules.NunSkin != 255) SkinSwapper.CurrentID = (SkinID)Rules.NunSkin; // the nun player's pick
            Session.Broadcast(Msg.Start, w =>
            {
                w.Write(Seed);
                w.Write((int)DifficultyController.CurrentDifficulty);
                w.Write(RandomizeItemLocationsMode.RandomModeEnabled);
                w.Write((int)TextureSwapper.CurrentID);
                w.Write((int)SkinSwapper.CurrentID);
                w.Write(PlayerPrefs.GetInt("timesPlayed", 0));
                w.Write(Session.Mode);
                Rules.Write(w);
            }, true);
            Begin();
        }

        static void Begin()
        {
            Session.GameStarted = true;
            Plugin.Log.LogInfo($"start: rules={Rules.Describe} seed={Seed} diff={DifficultyController.CurrentDifficulty} scenario={TextureSwapper.CurrentID} skin={SkinSwapper.CurrentID} random={RandomizeItemLocationsMode.RandomModeEnabled}");
            Plugin.Instance.StartCoroutine(WalkMenu());
        }

        static IEnumerator WalkMenu()
        {
            for (float t = 0; t < 60f; t += 0.5f)
            {
                if (SceneManager.GetActiveScene().name == GameScene) yield break;
                if (Click("Main/GoToPlaySettings")) { yield return new WaitForSecondsRealtime(0.5f); }
                if (Click("Holder (1)/Skip")) yield break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Plugin.Log.LogWarning("could not reach Skip button; load the game manually");
        }

        static bool Click(string suffix)
        {
            var b = Object.FindObjectsOfType<Button>().FirstOrDefault(x => x.isActiveAndEnabled && Plugin.Path_(x.transform).EndsWith(suffix));
            if (!b) return false;
            b.onClick.Invoke();
            return true;
        }

        // Same seed before the scene's random item/arrangement setup runs => same layout everywhere.
        [HarmonyPatch(typeof(ArrangementModeController), "Awake")]
        static class SeedPatch
        {
            static void Prefix()
            {
                if (!Session.Active || !Session.GameStarted) return;
                Random.InitState(Seed);
                Plugin.Log.LogInfo("seeded " + Seed);
            }
        }
    }
}
