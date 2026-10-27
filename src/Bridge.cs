using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


namespace NunMP
{
    // Dev bridge: agent writes BepInEx/nunmp_cmd_<pid>.txt, plugin answers in nunmp_out_<pid>.txt.
    // ponytail: file polling, a socket if latency ever matters.
    public class Bridge : MonoBehaviour
    {
        string cmdFile, outFile;
        float next;
        static float pressUntil;

        // "press": FSM keyboard checks (splash/menu "click to continue") read true briefly. No PID-targeted input otherwise.
        [HarmonyLib.HarmonyPatch(typeof(NodeCanvas.Tasks.Conditions.CheckKeyboardInput), "OnCheck")]
        static class PressPatch
        {
            static void Postfix(ref bool __result) { if (Time.unscaledTime < pressUntil) __result = true; }
        }

        void Awake()
        {
            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            cmdFile = Path.Combine(BepInEx.Paths.BepInExRootPath, $"nunmp_cmd_{pid}.txt");
            outFile = Path.Combine(BepInEx.Paths.BepInExRootPath, $"nunmp_out_{pid}.txt");
            File.WriteAllText(outFile, ""); // marker: tools find which install this pid runs from
        }

        void Update()
        {
            if (Time.unscaledTime < next || !File.Exists(cmdFile)) return;
            next = Time.unscaledTime + 0.25f;
            var lines = File.ReadAllLines(cmdFile);
            var sb = new StringBuilder();
            foreach (var l in lines.Where(l => l.Trim().Length > 0))
            {
                sb.AppendLine("> " + l);
                try { sb.AppendLine(Run(l.Trim())); } catch (System.Exception e) { sb.AppendLine("ERR " + e); }
            }
            File.AppendAllText(outFile, sb.ToString());
            File.Delete(cmdFile); // only after the reply is written: tools poll for this
        }

        System.Collections.IEnumerator Walk(float secs, float speed)
        {
            var pl = GameObject.FindGameObjectWithTag("Player");
            var cc = pl.GetComponent<CharacterController>();
            for (float t = 0; t < secs; t += Time.deltaTime)
            {
                cc.Move((pl.transform.forward * speed + Vector3.down * 2f) * Time.deltaTime);
                yield return null;
            }
        }

        System.Collections.IEnumerator Swing(Transform door, float deg)
        {
            var rb = door.GetComponent<Rigidbody>();
            for (int i = 0; i < 30; i++)
            {
                var r = Quaternion.Euler(0, deg / 30f, 0) * door.rotation;
                if (rb) rb.MoveRotation(r); else door.rotation = r;
                yield return new WaitForFixedUpdate();
            }
        }

        // Land on the floor under the target: CC bottom 5 cm above the hit (else we sink through geometry).
        static Vector3 Teleport(Vector3 to)
        {
            var pl = GameObject.FindGameObjectWithTag("Player");
            var cc = pl.GetComponent<CharacterController>();
            if (Physics.Raycast(to + Vector3.up * 1.5f, Vector3.down, out var fl, 6f, ~(1 << LayerMask.NameToLayer("Player")), QueryTriggerInteraction.Ignore))
                to = fl.point + Vector3.up * (cc.height * 0.5f - cc.center.y + 0.05f);
            cc.enabled = false; pl.transform.position = to; cc.enabled = true;
            return pl.transform.position;
        }

        string Run(string l)
        {
            var sp = l.IndexOf(' ');
            var cmd = sp < 0 ? l : l.Substring(0, sp);
            var arg = sp < 0 ? "" : l.Substring(sp + 1);
            switch (cmd)
            {
                case "buttons":
                    return string.Join("\n", FindObjectsOfType<Button>().Where(b => b.isActiveAndEnabled)
                        .Select(b => $"{Plugin.Path_(b.transform)} | {b.GetComponentInChildren<Text>()?.text}"));
                case "click":
                    var btn = FindObjectsOfType<Button>().FirstOrDefault(b => b.isActiveAndEnabled &&
                        (Plugin.Path_(b.transform).EndsWith(arg) || b.GetComponentInChildren<Text>()?.text == arg));
                    if (!btn) return "no button " + arg;
                    btn.onClick.Invoke();
                    return "clicked " + Plugin.Path_(btn.transform);
                case "scene": SceneManager.LoadScene(arg); return "loading " + arg;
                case "state":
                    var p = GameObject.FindGameObjectWithTag("Player");
                    return $"scene={SceneManager.GetActiveScene().name} timeScale={Time.timeScale} player={(p ? p.transform.position.ToString() : "none")} nun={(Sync.NunSync.Killer ? Sync.NunSync.Killer.transform.position.ToString() + " " + Sync.NunSync.HostState : "-")} {Sync.Targets.Debug}";
                case "layout": // hash of every interactable's position: equal on all peers => same random layout
                    var items = FindObjectsOfType<GrannyClone.Interactable>().Select(i => Plugin.Path_(i.transform) + i.transform.position.ToString("F0")).OrderBy(s => s).ToList();
                    File.WriteAllLines(Path.Combine(Plugin.DumpDir, $"layout_{System.Diagnostics.Process.GetCurrentProcess().Id}.txt"), items);
                    return $"seed={Sync.GameFlow.Seed} {items.Count} interactables hash={string.Join("|", items).GetHashCode():X8}";
                case "press":pressUntil = Time.unscaledTime + 0.3f; return "pressing Mouse0 for FSM conditions";
                case "host":Net.Session.StartHost(new Net.UdpTransport(true), "host"); return Net.Session.Status;
                case "join": Net.Session.StartClient(new Net.UdpTransport(false), "client"); return Net.Session.Status;
                case "start": Sync.GameFlow.HostStart(); return "starting";
                case "session":
                    return Net.Session.Status + "\n" + string.Join("\n", Net.Session.Peers.Values.Select(x => $"slot {x.Slot} {x.Name} {x.Id}"));
                case "dump": GetComponent<Plugin>().SendMessage("Dump"); return "dumped";
                case "animators":
                    return string.Join("\n", Resources.FindObjectsOfTypeAll<Animator>().Where(a => a.gameObject.scene.IsValid())
                        .Select(a => $"{Plugin.Path_(a.transform)} human={a.isHuman} avatar={a.avatar?.name} ctrl={a.runtimeAnimatorController?.name} active={a.gameObject.activeInHierarchy} smr={a.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length}"));
                case "params": // params <pathSuffix>
                    var an = Resources.FindObjectsOfTypeAll<Animator>().FirstOrDefault(x => x.gameObject.scene.IsValid() && Plugin.Path_(x.transform).EndsWith(arg));
                    if (!an) return "no animator";
                    var ac = an.runtimeAnimatorController;
                    return $"ctrl={ac?.name} clips={string.Join(",", ac ? ac.animationClips.Select(c => c.name).Distinct() : new string[0])}\n" +
                        (an.isActiveAndEnabled ? string.Join(",", an.parameters.Select(p => $"{p.name}:{p.type}")) : "(inactive, params need active animator)");
                case "shot": // shot <file.png> (this instance only)
                    ScreenCapture.CaptureScreenshot(arg); return "shot " + arg;
                case "tp": // tp x y z  |  tp nun  |  tp <slot>
                    {
                        var pl = GameObject.FindGameObjectWithTag("Player");
                        Vector3 to;
                        if (arg == "nun") to = Sync.NunSync.Killer.transform.position + Sync.NunSync.Killer.transform.forward * 2.2f + Vector3.up * 1.5f;
                        else if (byte.TryParse(arg, out var s) && Sync.PlayerSync.Puppets.TryGetValue(s, out var pp)) to = pp.transform.position + pp.transform.forward * 2 + Vector3.up;
                        else { var v = arg.Split(' ').Select(float.Parse).ToArray(); to = new Vector3(v[0], v[1], v[2]); }
                        return "at " + Teleport(to);
                    }
                case "tpraw": // tpraw x y z: exact position, no floor snap
                    {
                        var v = arg.Split(' ').Select(float.Parse).ToArray();
                        var pl = GameObject.FindGameObjectWithTag("Player");
                        var cc = pl.GetComponent<CharacterController>(); cc.enabled = false; pl.transform.position = new Vector3(v[0], v[1], v[2]); cc.enabled = true;
                        return "at " + pl.transform.position;
                    }
                case "walk": // walk <seconds> [speed]
                    {
                        var v = arg.Split(' ');
                        StartCoroutine(Walk(float.Parse(v[0]), v.Length > 1 ? float.Parse(v[1]) : 2f));
                        return "walking";
                    }
                case "dooropener": // her DoorOpener: settings + what its spherecast hits right now
                    {
                        var k = Sync.NunSync.Killer; var d = k ? k.GetComponent<GrannyClone.DoorOpener>() : null;
                        if (!d) return "no DoorOpener";
                        var t = k.transform;
                        var hitOk = Physics.SphereCast(t.position + t.TransformDirection(d.offset), d.radius, t.forward, out var hi, d.castDist, d.layerMask);
                        var mask = string.Join("|", Enumerable.Range(0, 32).Where(i => (d.layerMask.value & (1 << i)) != 0).Select(LayerMask.LayerToName));
                        return $"enabled={d.enabled} active={d.isActiveAndEnabled} offset={d.offset} r={d.radius} dist={d.castDist} force={d.force} mask={mask} hit={(hitOk ? Plugin.Path_(hi.collider.transform) + " kin=" + hi.collider.attachedRigidbody?.isKinematic + " canMove=" + hi.collider.GetComponent<GrannyClone.MoveInteractable>()?.CanMove + " door=" + (hi.collider.GetComponent<GrannyClone.Door>() != null) + " " + Sync.DoorSync.Describe(hi.collider.GetComponent<GrannyClone.MoveInteractable>()) : "none")} listeners={GrannyClone.MessagesGranny.DoorOpenEvent?.GetInvocationList().Length}";
                    }
                case "lookat": // lookat x y z: aim my body + camera at a point
                    {
                        var v = arg.Split(' ').Select(float.Parse).ToArray(); var tgt = new Vector3(v[0], v[1], v[2]);
                        var pl = GameObject.FindGameObjectWithTag("Player"); var cam = pl.GetComponent<GrannyClone.pReferences>().camera.transform;
                        var d = tgt - cam.position; var flat = new Vector3(d.x, 0, d.z);
                        pl.transform.rotation = Quaternion.LookRotation(flat);
                        cam.localRotation = Quaternion.Euler(-Mathf.Atan2(d.y, flat.magnitude) * Mathf.Rad2Deg, 0, 0);
                        return "looking";
                    }
                case "facenun":
                    {
                        var pl = GameObject.FindGameObjectWithTag("Player");
                        var dir = Sync.NunSync.Killer.transform.position - pl.transform.position; dir.y = 0;
                        pl.transform.rotation = Quaternion.LookRotation(dir);
                        pl.GetComponent<GrannyClone.pReferences>().camera.transform.localRotation = Quaternion.identity;
                        return "facing the nun";
                    }
                case "face": // face <slot>: turn the local player toward that puppet
                    {
                        var pl = GameObject.FindGameObjectWithTag("Player");
                        if (!byte.TryParse(arg, out var s) || !Sync.PlayerSync.Puppets.TryGetValue(s, out var pp)) return "no puppet";
                        var dir = pp.transform.position - pl.transform.position; dir.y = 0;
                        pl.transform.rotation = Quaternion.LookRotation(dir);
                        var cam = pl.GetComponent<GrannyClone.pReferences>().camera.transform;
                        cam.localRotation = Quaternion.identity;
                        return "facing " + pp.transform.position;
                    }
                case "rules": // rules <lives 0-9> <shared|each> <spectate|gameover>
                    {
                        var v = arg.Split(' ');
                        Sync.Rules.Lives = int.Parse(v[0]);
                        Sync.Rules.Mode = v[1] == "each" ? Sync.Rules.LivesMode.PerPlayer : Sync.Rules.LivesMode.Shared;
                        Sync.Rules.Out = v[2] == "gameover" ? Sync.Rules.OutMode.GameOverAll : Sync.Rules.OutMode.Spectate;
                        if (Net.Session.IsHost) Net.Session.SendRoster();
                        return Sync.Rules.Describe;
                    }
                case "die":Sync.PlayerSync.Local.health.Damage(500f); return "ouch";
                case "steamhost": Net.SteamLobby.Create(Net.Session.Mode); return "creating lobby";
                case "steamstatus": return $"ready={Net.SteamLobby.Ready} err='{Net.SteamLobby.Error}' lobby={Net.SteamLobby.Lobby.m_SteamID} members={Net.SteamLobby.Members.Count} session='{Net.Session.Status}' {Net.SteamLobby.Debug}";
                case "lives":
                    return $"pool={Sync.LivesSync.Pool} local={(LivesSystem.instance ? LivesSystem.instance.currentLives : -9)} spectating={Sync.LivesSync.Spectating} hp={Sync.PlayerSync.Local?.health?.HealthMeter} scene={SceneManager.GetActiveScene().name}";
                case "turn": // turn <degrees>
                    { var pl = GameObject.FindGameObjectWithTag("Player"); pl.transform.Rotate(0, float.Parse(arg), 0); return "yaw " + pl.transform.eulerAngles.y; }
                case "inspect": // inspect <pathSuffix>: hierarchy with layers + colliders
                    {
                        var t0 = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.gameObject.scene.IsValid() && Plugin.Path_(t).EndsWith(arg));
                        if (!t0) return "no object";
                        var sb2 = new StringBuilder($"queriesHitTriggers={Physics.queriesHitTriggers}\n");
                        foreach (var t in t0.GetComponentsInChildren<Transform>(true))
                        {
                            var cols = string.Join(",", t.GetComponents<Collider>().Select(c => $"{c.GetType().Name}{(c.isTrigger ? "(T)" : "")}"));
                            sb2.AppendLine($"{Plugin.Path_(t).Substring(Plugin.Path_(t0).Length)} layer={LayerMask.LayerToName(t.gameObject.layer)} local={t.localPosition} {cols}");
                        }
                        foreach (var rd in Resources.FindObjectsOfTypeAll<GrannyClone.RaycastDamage>().Where(x => x.gameObject.scene.IsValid()))
                        {
                            var m = (LayerMask)typeof(GrannyClone.RaycastDamage).GetField("layers", (System.Reflection.BindingFlags)~0).GetValue(rd);
                            sb2.AppendLine($"RaycastDamage {Plugin.Path_(rd.gameObject.transform)} active={rd.gameObject.activeInHierarchy} layers=" + string.Join("|", Enumerable.Range(0, 32).Where(i => (m.value & (1 << i)) != 0).Select(LayerMask.LayerToName)));
                        }
                        return sb2.ToString();
                    }
                case "colliders": // colliders <pathSuffix>
                    {
                        var t0 = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.gameObject.scene.IsValid() && Plugin.Path_(t).EndsWith(arg));
                        if (!t0) return "no object";
                        return string.Join("\n", t0.GetComponentsInChildren<Collider>(true).Select(c => $"{Plugin.Path_(c.transform)} trig={c.isTrigger} center={c.bounds.center} size={c.bounds.size} on={c.enabled && c.gameObject.activeInHierarchy}"));
                    }
                case "event": // event <ownerPathSuffix> <eventName>
                    {
                        var sp2 = arg.LastIndexOf(' ');
                        var own = FindObjectsOfType<NodeCanvas.Framework.GraphOwner>().FirstOrDefault(o => Plugin.Path_(o.transform).EndsWith(arg.Substring(0, sp2)));
                        if (!own) return "no owner";
                        own.SendEvent(arg.Substring(sp2 + 1));
                        return "sent";
                    }
                case "fsm": // fsm <ownerSuffix>   |  trigger <ownerSuffix>|<stateName>
                case "trigger":
                    {
                        var parts = arg.Split('|');
                        var own = FindObjectsOfType<NodeCanvas.StateMachines.FSMOwner>().FirstOrDefault(o => Plugin.Path_(o.transform).EndsWith(parts[0]));
                        if (!own) return "no fsm";
                        var f = (NodeCanvas.StateMachines.FSM)own.graph;
                        if (cmd == "trigger") f.TriggerState(parts[1]);
                        var cur = f.currentState;
                        return $"running={own.isRunning} paused={own.isPaused} state#{(cur == null ? -1 : f.allNodes.IndexOf(cur))} '{cur?.name}'";
                    }
                case "faststart": // dev: nun's first-play start delay is 500-600 s; make it ~4 s
                    {
                        var k = GameObject.Find("Characters/Killer Stuff new/Killer").GetComponent<NodeCanvas.Framework.GraphOwner>();
                        k.blackboard.SetValue("StartDelay", new Vector2(3, 5));
                        k.SendEvent("StartDelay");
                        return "nun starts in ~4s";
                    }
                case "objrefs": // objrefs <ownerPathSuffix>: graph's serialized object references (+ AudioSource info)
                    {
                        var own = Resources.FindObjectsOfTypeAll<NodeCanvas.Framework.GraphOwner>().FirstOrDefault(o => o.gameObject.scene.IsValid() && Plugin.Path_(o.transform).EndsWith(arg));
                        var refs = own?.graph?._objectReferences;
                        if (refs == null) return "no refs";
                        return string.Join("\n", refs.Select((o, i) =>
                        {
                            var s = $"[{i}] {o?.GetType().Name} {(o is Component c ? Plugin.Path_(c.transform) : o is GameObject g ? Plugin.Path_(g.transform) : o?.name)}";
                            var au = o is AudioSource a0 ? a0 : o is GameObject g2 ? g2.GetComponent<AudioSource>() : o is Component c2 ? c2.GetComponent<AudioSource>() : null;
                            if (au) s += $"  audio clip={au.clip?.name} 3d={au.spatialBlend:0.#} loop={au.loop}";
                            return s;
                        }));
                    }
                case "mode": Net.Session.Mode = arg; if (Net.Session.IsHost) Net.Session.SendRoster(); return "mode " + arg;
                case "nunslot": Sync.Rules.NunSlot = byte.Parse(arg); if (Net.Session.IsHost) Net.Session.SendRoster(); return Sync.Rules.Describe;
                case "nunwalk": { var v = arg.Split(' ').Select(float.Parse).ToArray(); Sync.NunPlayer.TestMove = new Vector3(v[0], 0, v[1]); Sync.NunPlayer.TestUntil = Time.time + v[2]; return "walking"; }
                case "nunkey": Sync.NunPlayer.TestKey = (KeyCode)System.Enum.Parse(typeof(KeyCode), arg); return "pressed " + arg;
                case "nunplace": { var v = arg.Split(' ').Select(float.Parse).ToArray(); Sync.NunPlayer.TestPlace(new Vector3(v[0], v[1], v[2]), v[3], v[4]); return "placed"; }
                case "nunlean": Sync.NunPlayer.TestLean = float.Parse(arg); Sync.NunPlayer.TestMove = Vector3.zero; Sync.NunPlayer.TestUntil = Time.time + 4; return "leaning";
                case "nuncrouch": Sync.NunPlayer.TestCrouch = arg == "1"; return "crouch " + arg;
                case "tape": { var t = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(x => x.gameObject.scene.IsValid() && Plugin.Path_(x) == "Global/Video_Cameras/Vhs Damage Cam Fsm/Tape Damage"); return "tape=" + (t ? t.gameObject.activeSelf.ToString() : "?"); }
                case "aiwarp": { var v = arg.Split(' ').Select(float.Parse).ToArray(); var ag = Sync.NunSync.Killer.GetComponent<UnityEngine.AI.NavMeshAgent>(); return ag.Warp(new Vector3(v[0], v[1], v[2])) ? "warped" : "warp failed"; }
                case "nunsprint": Sync.NunPlayer.TestSprint = arg == "1"; return "sprint " + arg;
                case "nun": return $"iAmNun={Sync.NunMode.IAmNun} active={Sync.NunMode.Active} aim={Sync.NunPlayer.AimSlot} stamina={Sync.NunPlayer.Stamina:0.00} sprinting={Sync.NunMode.Sprinting} sneak={Sync.NunMode.Sneaking} lurk={Sync.NunPlayer.Lurking} seenAgo={Sync.NunPlayer.SeenAgo:0} pings={Sync.NunPlayer.PingsOn} shot={Sync.NunPlayer.ShotLeft:0} fov={(Camera.main ? Camera.main.fieldOfView : 0):0} nunpos={(Sync.NunSync.Killer ? Sync.NunSync.Killer.transform.position.ToString() : "-")} {Sync.NunMode.SeeDebug}";
                case "pref": Net.Session.SendPref(byte.Parse(arg)); if (Net.Session.IsHost) { Sync.Rules.NunSkin = byte.Parse(arg); Net.Session.SendRoster(); } return "pref " + arg;
                case "skin": return $"current={SkinSwapper.CurrentID} rule={Sync.Rules.NunSkin} model={(Sync.NunSync.Killer ? string.Join(",", Sync.NunSync.Killer.transform.Find("Model").Cast<Transform>().Where(t => t.gameObject.activeSelf).Select(t => t.name)) : "-")}";
                case "takeradio": Sync.Radio.Take(int.Parse(arg)); return Sync.Radio.Debug;
                case "radiotone": Sync.Voice.TestRadio = true; Sync.Voice.ToneTest(float.Parse(arg)); return "radio tone";
                case "mictest": Sync.Voice.MicTest = arg == "1"; return "mictest " + arg;
                case "micstatus": return $"status={Sync.Voice.Status} level={Sync.Voice.MyLevel:0.000} tx={Sync.Voice.Transmitting} result={Sync.Voice.LastResult} decode={Sync.Voice.LastDecode} pkt={Sync.Voice.LastPacket}B pcm={Sync.Voice.LastPcm}B peak={Sync.Voice.Peak}";
                case "mics": return string.Join(" | ", Microphone.devices) + " -> using " + Sync.DirectMic.Device + " direct=" + Sync.DirectMic.UseDirect + " boost=" + Sync.DirectMic.Boost;
                case "testptt": Sync.Voice.TestPTT = arg == "1"; return "ptt " + arg;
                case "f2": UI.LobbyMenu.TestToggle = true; return "toggled";
                case "cursor": return $"lock={Cursor.lockState} visible={Cursor.visible} canMouseLook={Sync.PlayerSync.Local?.firstPersonController?.canMouseLook} panel={UI.LobbyMenu.Open}";
                case "notice": { Sync.NunPlayer.PlayNotice(); var n = GameObject.Find("NunMP Notice")?.GetComponent<AudioSource>(); return n ? $"clip={n.clip?.name} vol={n.volume} playing={n.isPlaying} mixer={(n.outputAudioMixerGroup ? n.outputAudioMixerGroup.name : "none")}" : "no notice source"; }
                case "tone": Sync.Voice.ToneTest(float.Parse(arg)); return "tone for " + arg + "s";
                case "shootnun": // dev: what the revolver does when its ray hits her (GunDamageEvent on her collider)
                    {
                        var k = Sync.NunSync.Killer; var col = k ? k.GetComponentsInChildren<Collider>().FirstOrDefault(c => !c.isTrigger) : null;
                        if (!col) return "no nun collider";
                        GrannyClone.MessagesGranny.GunDamageEvent?.Invoke(col, 0f, col.bounds.center); return "shot " + Plugin.Path_(col.transform);
                    }
                case "opustone": Sync.DirectMic.ToneUntil = Time.unscaledTime + float.Parse(arg); return "opus tone for " + arg + "s";
                case "voice": return $"sent={Sync.Voice.SentBytes}B/{Sync.Voice.SentTo}pk tx={Sync.Voice.Transmitting} level={Sync.Voice.MyLevel:0.00} ptt={Sync.Voice.PushToTalk} | {Sync.Voice.Debug}";
                case "audio": // nun audio sources currently playing
                    {
                        var root = GameObject.Find("Characters/Killer Stuff new/Audio Follow Killer");
                        return root ? string.Join(" | ", root.GetComponentsInChildren<AudioSource>(true).Where(a => a.isPlaying).Select(a => $"{a.name}{(a.mute ? "(muted)" : "")} v={a.volume:0.##}")) : "no root";
                    }
                case "srcinfo": // srcinfo <pathSuffix>: how a game AudioSource is set up (mixer, spatializer, rolloff, siblings)
                    {
                        var src0 = Resources.FindObjectsOfTypeAll<AudioSource>().FirstOrDefault(x => x.gameObject.scene.IsValid() && Plugin.Path_(x.transform).EndsWith(arg));
                        if (!src0) return "no source";
                        return $"{Plugin.Path_(src0.transform)} out={src0.outputAudioMixerGroup?.name}/{src0.outputAudioMixerGroup?.audioMixer?.name} spatialize={src0.spatialize} blend={src0.spatialBlend} rolloff={src0.rolloffMode} min={src0.minDistance} max={src0.maxDistance} reverbZoneMix={src0.reverbZoneMix} comps={string.Join(",", src0.GetComponents<Component>().Select(c => c.GetType().Name))}";
                    }
                case "which": // which <ComponentTypeName>
                    return string.Join("\n", Resources.FindObjectsOfTypeAll<Component>().Where(c => c && c.gameObject.scene.IsValid() && c.GetType().Name == arg).Take(20).Select(c => Plugin.Path_(c.transform)));
                case "events": // events <pathSuffix>|<Component>|<UnityEvent field>  (or state:<i> for StateInteractable states)
                    {
                        var a4 = arg.Split('|');
                        var tr = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.gameObject.scene.IsValid() && Plugin.Path_(t).EndsWith(a4[0]));
                        var comp = tr ? tr.GetComponents<Component>().FirstOrDefault(x => x && x.GetType().Name == a4[1]) : null;
                        if (!comp) return "no component";
                        UnityEngine.Events.UnityEventBase ev;
                        if (a4[2].StartsWith("state:")) ev = ((GrannyClone.StateInteractable)comp).states[int.Parse(a4[2].Substring(6))].OnStartEvent;
                        else ev = comp.GetType().GetField(a4[2], (System.Reflection.BindingFlags)~0)?.GetValue(comp) as UnityEngine.Events.UnityEventBase;
                        if (ev == null) return "no event";
                        return string.Join("\n", Enumerable.Range(0, ev.GetPersistentEventCount()).Select(i =>
                        {
                            var tg = ev.GetPersistentTarget(i);
                            var tp = tg is Component tc ? Plugin.Path_(tc.transform) : tg is GameObject tgo ? Plugin.Path_(tgo.transform) : tg?.name;
                            return $"{tg?.GetType().Name} {tp} .{ev.GetPersistentMethodName(i)}";
                        }));
                    }
                case "get":// get <pathSuffix>|<Component>|<field>  (component "bb" = graph blackboard variable)
                    {
                        var a3 = arg.Split('|');
                        var tr = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.gameObject.scene.IsValid() && Plugin.Path_(t).EndsWith(a3[0]));
                        if (!tr) return "no object";
                        if (a3[1] == "bb") return tr.GetComponent<NodeCanvas.Framework.GraphOwner>()?.blackboard?.GetVariable(a3[2])?.value?.ToString() ?? "null";
                        var comp = tr.GetComponents<Component>().FirstOrDefault(x => x && x.GetType().Name == a3[1]);
                        if (!comp) return "no component";
                        var fi = comp.GetType().GetField(a3[2], (System.Reflection.BindingFlags)~0);
                        var val = fi?.GetValue(comp); return fi == null ? "no field" : val is System.Array arr ? string.Join(", ", arr.Cast<object>().Select(o => o is Component cc2 && cc2 ? Plugin.Path_(cc2.transform) + " layer=" + LayerMask.LayerToName(cc2.gameObject.layer) + " cols=" + string.Join(",", cc2.GetComponents<Collider>().Select(x => x.GetType().Name + (x.isTrigger ? "(T)" : ""))) : o?.ToString())) : $"{val}";
                    }
                case "interact": // interact <pathSuffix>  (as if the local player pressed Action on it)
                    {
                        var it = FindObjectsOfType<GrannyClone.Interactable>().FirstOrDefault(x => Plugin.Path_(x.transform).EndsWith(arg));
                        if (!it) return "no interactable";
                        it.Interact();
                        return $"interacted {it.GetType().Name} {Plugin.Path_(it.transform)}";
                    }
                case "push": // push <pathSuffix>|<degrees>: swing a door like walking into it (teleports me next to it first)
                    {
                        var pa = arg.Split('|');
                        var door = FindObjectsOfType<GrannyClone.MoveInteractable>().FirstOrDefault(x => Plugin.Path_(x.transform).EndsWith(pa[0]));
                        if (!door) return "no door";
                        var pl = GameObject.FindGameObjectWithTag("Player");
                        Teleport(door.transform.position + door.transform.forward * 1.5f + Vector3.up);
                        StartCoroutine(Swing(door.transform, float.Parse(pa[1])));
                        return "pushing " + Plugin.Path_(door.transform);
                    }
                case "interactables": // interactables <filter>
                    return string.Join("\n", FindObjectsOfType<GrannyClone.Interactable>().Where(x => Plugin.Path_(x.transform).ToLower().Contains(arg.ToLower()))
                        .Take(30).Select(x => $"{x.GetType().Name} {Plugin.Path_(x.transform)} @ {x.transform.position} yaw={x.transform.eulerAngles.y:0}"));
                case "meshes": // every skinned mesh in scene (incl. inactive) + loaded-but-unplaced ones
                    return string.Join("\n", Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>()
                        .Select(s => $"{(s.gameObject.scene.IsValid() ? Plugin.Path_(s.transform) : "[asset] " + s.name)} mesh={s.sharedMesh?.name} bones={s.bones.Length} mat={s.sharedMaterial?.name}")
                        .Where(x => !x.Contains("Ghost child") && !x.Contains("Corpse")).Distinct().Take(120));
                case "find":
                    return string.Join("\n", Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.gameObject.scene.IsValid() && t.name.ToLower().Contains(arg.ToLower()))
                        .Take(40).Select(t => $"{Plugin.Path_(t)} active={t.gameObject.activeInHierarchy} pos={t.position} yaw={t.eulerAngles.y:0}"));
                case "call": // call <objectPathSuffix>|<Component>|<method>  (activates the object if needed)
                    var a = arg.Split('|');
                    var go = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.gameObject.scene.IsValid() && Plugin.Path_(t).EndsWith(a[0]));
                    if (!go) return "no object";
                    if (!go.gameObject.activeSelf) go.gameObject.SetActive(true);
                    var c = go.GetComponents<Component>().FirstOrDefault(x => x && x.GetType().Name == a[1]);
                    if (!c) return "no component";
                    c.GetType().GetMethod(a[2], (System.Reflection.BindingFlags)~0, null, System.Type.EmptyTypes, null).Invoke(c, null);
                    return "called";
            }
            return "unknown " + cmd;
        }
    }
}
