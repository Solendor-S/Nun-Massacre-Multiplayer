using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using NodeCanvas.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NunMP
{
    [BepInPlugin("sargo.nunmp", "NunMP", Net.Session.Version)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static Plugin Instance;
        public static string DumpDir;
        bool overlay; // F3

        void Awake()
        {
            Log = Logger;
            Instance = this;
            DumpDir = Path.Combine(Paths.BepInExRootPath, "nunmp_dump");
            Directory.CreateDirectory(DumpDir);
            Application.runInBackground = true;
            new Harmony("sargo.nunmp").PatchAll();
            gameObject.AddComponent<Bridge>();
            gameObject.AddComponent<UI.LobbyMenu>();
            Net.SteamLobby.Init(Path.GetDirectoryName(Info.Location));
            Sync.GameFlow.Init();
            Sync.PlayerSync.Init();
            Sync.NunSync.Init();
            gameObject.AddComponent<Sync.PlayerSync>();
            gameObject.AddComponent<Sync.NunSync>();
            Sync.Targets.Init();
            Sync.WorldSync.Init();
            Sync.LivesSync.Init();
            Sync.Voice.Init();
            Sync.Radio.Init();
            Sync.NunMode.Init();
            gameObject.AddComponent<Sync.NunMode>();
            gameObject.AddComponent<Sync.NunPlayer>();
            gameObject.AddComponent<Sync.Voice>();
            gameObject.AddComponent<Sync.LivesSync>();
            gameObject.AddComponent<Sync.Targets>();
            gameObject.AddComponent<Sync.DoorSync>();
            SceneManager.sceneLoaded += (s, m) =>
            {
                Log.LogInfo($"scene loaded: {s.name} ({s.buildIndex})");
                // graph dumps are dev-only: F8
            };
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3)) overlay = !overlay;
            if (Input.GetKeyDown(KeyCode.F8)) Dump();
            Net.SteamLobby.Tick();
            Net.Session.Tick();
            Sync.WorldSync.Tick();
        }

        void OnApplicationQuit() => Net.Session.Stop();

        void OnGUI()
        {
            if (!overlay) return;
            var p = GameObject.FindGameObjectWithTag("Player");
            var sb = new StringBuilder();
            sb.AppendLine($"NunMP {Net.Session.Version} [{Net.Session.Status}] scene={SceneManager.GetActiveScene().name}  t={Time.timeScale:0.##}");
            if (p) sb.AppendLine($"player {p.name} {p.transform.position}");
            if (Net.Session.Active)
            {
                sb.AppendLine($"slot {Net.Session.MySlot} {(Net.Session.IsHost ? "HOST" : "client")}  nun state: {Sync.NunSync.HostState}");
                foreach (var kv in Sync.PlayerSync.Puppets) if (kv.Value) sb.AppendLine($"  puppet {kv.Key} @ {kv.Value.transform.position} flags={kv.Value.Flags}");
            }
            foreach (var o in FindObjectsOfType<GraphOwner>().Where(o => o.isRunning && o.name == "Killer"))
                sb.AppendLine($"graph {o.name}: {o.graph?.name} @ {o.transform.position}");
            GUI.Label(new Rect(8, 8, 900, 400), sb.ToString());
        }

        // ponytail: one-shot research dump; delete once the nun's graphs are mapped.
        void Dump()
        {
            var scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            foreach (var o in FindObjectsOfType<GraphOwner>(true))
            {
                sb.AppendLine($"== {Path_(o.transform)}  running={o.isRunning} graph={o.graph?.GetType().Name}:{o.graph?.name}");
                foreach (var c in o.GetComponents<Component>()) sb.AppendLine($"   comp {c?.GetType().FullName}");
                if (o.blackboard?.variables != null)
                    foreach (var kv in o.blackboard.variables)
                        sb.AppendLine($"   bb {kv.Key} : {kv.Value.varType?.Name} = {Safe(kv.Value.value)}");
                if (o.graph != null)
                {
                    var file = Path.Combine(DumpDir, $"{scene.name}_{o.name}_{o.GetInstanceID()}.json".Replace(' ', '_'));
                    try { File.WriteAllText(file, o.graph.Serialize(true, null)); } catch (System.Exception e) { sb.AppendLine("   serialize fail " + e.Message); }
                }
            }
            foreach (var p in GameObject.FindGameObjectsWithTag("Player"))
            {
                sb.AppendLine($"== PLAYER {Path_(p.transform)}");
                foreach (var c in p.GetComponents<Component>()) sb.AppendLine($"   comp {c?.GetType().FullName}");
            }
            sb.AppendLine("== ROOTS");
            foreach (var r in scene.GetRootGameObjects()) Tree(sb, r.transform, 0, 3);
            File.WriteAllText(Path.Combine(DumpDir, $"{scene.name}_summary.txt".Replace(' ', '_')), sb.ToString());
            Log.LogInfo("dumped " + scene.name);
        }

        static void Tree(StringBuilder sb, Transform t, int d, int max)
        {
            var comps = string.Join(",", t.GetComponents<Component>().Where(c => c && !(c is Transform)).Select(c => c.GetType().Name));
            sb.AppendLine($"{new string(' ', d * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " [off]")} [{t.tag}] {comps}");
            if (d < max) foreach (Transform c in t) Tree(sb, c, d + 1, max);
        }

        public static string Path_(Transform t) => t.parent ? Path_(t.parent) + "/" + t.name : t.name;
        static string Safe(object o) { try { return o is Object u ? (u ? u.name : "null") : o?.ToString(); } catch { return "?"; } }
    }
}
