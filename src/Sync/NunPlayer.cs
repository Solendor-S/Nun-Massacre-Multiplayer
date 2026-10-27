using System.Linq;
using GrannyClone;
using NunMP.Net;
using UnityEngine;
using UnityEngine.AI;

namespace NunMP.Sync
{
    // Local controller when I am the nun: first-person through her eyes, player-style physics movement (so she
    // can crawl vents and cross the plank, which the AI's navmesh can't), peeking, and her powers.
    public class NunPlayer : MonoBehaviour
    {
        // tuning knobs
        const float Walk = 2.3f, Run = 4.6f, Sneak = 1.1f, CrouchRun = 1.55f, Reach = 2.6f, HitDamage = 40f, SwingCd = 1.3f;
        const float StaminaDrain = 0.28f, StaminaRegen = 0.12f, PingCalm = 90f;
        const float StandHeight = 1.75f, CrouchHeight = 0.9f, EyeStand = 1.62f, EyeCrouch = 0.78f;
        const float PullCdHit = 6f, PullCdMiss = 30f;
        const float ShotStun = 25f, ShotStagger = 2.5f;  // revolver: no swing/powers/aim/pings for ShotStun, slowed for ShotStagger
        const float LurkAfter = 2.5f, LurkFov = 10f;   // stand still this long -> lurking (no static for survivors), view narrows

        public static float Stamina = 1f, Lean;     // Lean -1 (left) .. 1 (right), streamed so her body peeks too
        public static byte AimSlot;                 // survivor in reach + in front
        static float yaw, pitch, attackUntil, nextAttack, eye = EyeStand, vy, hitFlashUntil;
        static float cdTrap, cdPull, cdGas, cdScream, cdLights, gasOffAt;
        static int trapsLeft = 4;
        static float lastSeen;   // last time any survivor had her on screen
        public static float SeenAgo => Time.time - lastSeen; public static bool PingsOn => SeenAgo > PingCalm && ShotLeft <= 0;
        static bool setUp, crouched, winded;
        public static bool Lurking; static float stillSince, baseFov = -1;
        static float shotUntil, staggerUntil; public static float ShotLeft => Mathf.Max(0, shotUntil - Time.time);
        static Color oldAmbient; static UnityEngine.Rendering.AmbientMode oldMode;
        static CharacterController cc;
        Animator anim;
        static string msg = ""; static float msgUntil;
        // dev bridge hooks (nunwalk / nunkey / nunplace): drive her without a keyboard
        public static Vector3 TestMove; public static float TestUntil, TestLean; public static bool TestCrouch, TestSprint; public static KeyCode TestKey = KeyCode.None;
        static bool Down(KeyCode k) { if (TestKey == k) { TestKey = KeyCode.None; return true; } return Input.GetKeyDown(k); }
        public static void TestPlace(Vector3 p, float y, float pi)
        {
            var k = NunSync.Killer; if (!k) return;
            if (cc) cc.enabled = false; k.transform.position = p; if (cc) cc.enabled = true;
            yaw = y; pitch = pi;
        }

        void Update()
        {
            if (!NunMode.IAmNun) { if (setUp) TearDown(); return; }
            var k = NunSync.Killer;
            if (!k) return;
            if (!setUp) SetUp(k);
            Look();
            Move(k);
            Powers(k);
            Sight(k);
        }

        void SetUp(GameObject k)
        {
            setUp = true;
            anim = k.GetComponent<Animator>();
            // player physics instead of the navmesh: vents, plank bridges, anywhere a survivor can go
            var agent = k.GetComponent<NavMeshAgent>(); if (agent) agent.enabled = false;
            foreach (var c in k.GetComponents<Collider>()) if (!c.isTrigger) c.enabled = false; // keep DoorOpener's trigger
            // her AI layer doesn't collide with the level (the navmesh did that job); collide like a player instead
            Plugin.Log.LogInfo("nun layer was " + LayerMask.LayerToName(k.layer));
            k.layer = LayerMask.NameToLayer("Player");
            cc = k.GetComponent<CharacterController>() ?? k.AddComponent<CharacterController>();
            cc.radius = 0.3f; cc.height = StandHeight; cc.center = new Vector3(0, StandHeight / 2, 0);
            cc.stepOffset = 0.35f; cc.slopeLimit = 50; cc.skinWidth = 0.04f; cc.enabled = true;
            yaw = k.transform.eulerAngles.y; pitch = 0; vy = 0;
            // start on solid ground
            if (Physics.Raycast(k.transform.position + Vector3.up * 1.5f, Vector3.down, out var gnd, 5f, ~(1 << LayerMask.NameToLayer("Player")), QueryTriggerInteraction.Ignore))
            { cc.enabled = false; k.transform.position = gnd.point + Vector3.up * 0.05f; cc.enabled = true; }
            // my survivor body sleeps while I'm her
            var me = PlayerSync.Local ? PlayerSync.Local : GameObject.FindGameObjectWithTag("Player")?.GetComponent<pReferences>();
            if (me)
            {
                me.playerStates.ChangeState(PlayerStates.States.Cutscene);
                if (me.characterController) me.characterController.enabled = false;
                foreach (var c in me.GetComponents<Collider>()) c.enabled = false;
            }
            // see through her eyes without seeing the inside of her head
            foreach (var r in k.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            trapsLeft = 4; Stamina = 1; lastSeen = Time.time; stillSince = Time.time;
            // dark vision: the nun knows her halls (local only; survivors still see a dark world)
            oldAmbient = RenderSettings.ambientLight; oldMode = RenderSettings.ambientMode;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.15f, 0.19f);
            Plugin.Log.LogInfo("I am the nun");
            Say("YOU ARE THE NUN.  Hunt them.", 6);
        }

        void TearDown()
        {
            setUp = false; Lurking = false;
            if (baseFov > 0 && Camera.main) Camera.main.fieldOfView = baseFov; baseFov = -1;
            RenderSettings.ambientMode = oldMode; RenderSettings.ambientLight = oldAmbient;
            var me = PlayerSync.Local;
            if (me && me.characterController) me.characterController.enabled = true;
        }

        static float MouseAxis(int axis, string name)
        {
            try { var v = Input.GetAxisRaw(name); if (v != 0) return v; } catch { }
            try { return Rewired.ReInput.controllers.Mouse.GetAxisRaw(axis); } catch { return 0; }
        }

        void Look()
        {
            if (UI.LobbyMenu.Open) return;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            yaw += MouseAxis(0, "Mouse X") * 2.2f;
            pitch = Mathf.Clamp(pitch - MouseAxis(1, "Mouse Y") * 2.2f, -75, 75);
            float want = Time.time < TestUntil && TestLean != 0 ? TestLean : (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
            Lean = Mathf.MoveTowards(Lean, want, Time.deltaTime * 4f);
        }

        void LateUpdate()
        {
            if (!NunMode.IAmNun || !NunSync.Killer) return;
            var cam = Camera.main;
            if (!cam) return;
            var k = NunSync.Killer.transform;
            k.rotation = Quaternion.Euler(0, yaw, 0);
            eye = Mathf.MoveTowards(eye, crouched ? EyeCrouch : EyeStand, Time.deltaTime * 3f);
            // peek: slide the eye sideways (not through walls) and tilt
            var side = k.right * (0.45f * Lean);
            var basePos = k.position + Vector3.up * eye + k.forward * 0.18f;
            if (Lean != 0 && Physics.SphereCast(basePos, 0.12f, side.normalized, out var wall, side.magnitude, ~(1 << LayerMask.NameToLayer("Player")), QueryTriggerInteraction.Ignore))
                side = side.normalized * Mathf.Max(0, wall.distance - 0.05f);
            cam.transform.SetPositionAndRotation(basePos + side, Quaternion.Euler(pitch, yaw, -12f * Lean));
            if (baseFov < 0) baseFov = cam.fieldOfView;
            cam.fieldOfView = Mathf.MoveTowards(cam.fieldOfView, Lurking ? baseFov - LurkFov : baseFov, Time.deltaTime * 20f);
        }

        void Move(GameObject k)
        {
            var input = new Vector3((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), 0, (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            if (Time.time < TestUntil) input = TestMove;
            if (input.sqrMagnitude > 1) input.Normalize();
            bool wantCrouch = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C) || TestCrouch;
            // stand up only if there's headroom (e.g. not inside a vent)
            if (crouched && !wantCrouch && Physics.SphereCast(k.transform.position + Vector3.up * (CrouchHeight - 0.3f), 0.28f, Vector3.up, out _, StandHeight - CrouchHeight + 0.05f, ~(1 << LayerMask.NameToLayer("Player")), QueryTriggerInteraction.Ignore)) wantCrouch = true;
            if (wantCrouch != crouched) { crouched = wantCrouch; var hgt = crouched ? CrouchHeight : StandHeight; cc.height = hgt; cc.center = new Vector3(0, hgt / 2, 0); }
            // Shift sprints, crouched too (like the vanilla nun): a crouch-run is only a bit faster than a
            // crouching survivor, burns stamina twice as fast, and is NOT sneaky (survivors get the static).
            // fully drained = winded: no sprinting again until a fifth of the bar has refilled
            if (Stamina <= 0.02f) winded = true; else if (Stamina >= 0.2f) winded = false;
            NunMode.Sprinting = (Input.GetKey(KeyCode.LeftShift) || TestSprint) && input.sqrMagnitude > 0 && !winded;
            // standing still (no steps, no swing) for a few seconds = lurking: survivors' screens stay clean, like the AI nun's
            if (input.sqrMagnitude > 0 || Time.time < attackUntil) stillSince = Time.time;
            Lurking = Time.time - stillSince > LurkAfter;
            NunMode.Sneaking = (crouched && !NunMode.Sprinting) || Lurking;
            float drain = crouched ? StaminaDrain * 2f : StaminaDrain;
            Stamina = Mathf.Clamp01(Stamina + (NunMode.Sprinting ? -drain : StaminaRegen) * Time.deltaTime);
            float speed = crouched ? (NunMode.Sprinting ? CrouchRun : Sneak) : NunMode.Sprinting ? Run : Walk;
            if (Time.time < staggerUntil) speed *= 0.35f;
            var dir = Quaternion.Euler(0, yaw, 0) * input * speed;
            vy = cc.isGrounded ? -1f : vy - 9.8f * Time.deltaTime;
            cc.Move((dir + Vector3.up * vy) * Time.deltaTime);
            if (anim)
            {
                anim.SetFloat("Forward", input.sqrMagnitude > 0 ? speed / Run : 0, 0.1f, Time.deltaTime);
                anim.SetFloat("Turn", 0);
                anim.SetBool("Crouch", crouched);
                anim.SetBool("OnGround", true);
                anim.SetBool("Attack", Time.time < attackUntil);
            }
        }

        void Powers(GameObject k)
        {
            if (gasOffAt > 0 && Time.time > gasOffAt) { gasOffAt = 0; NunMode.Send(NEv.Gas, w => w.Write(false)); }
            if (Time.time < shotUntil) { AimSlot = 255; return; } // shot: hurt and half-blind, nothing but running away
            if (UI.LobbyMenu.Open) return;
            var pos = k.transform.position;
            AimSlot = Aim(k, Reach, 60f);
            // LMB: swing. Damage is confirmed here (what the nun player sees) and applied by the victim.
            if (Down(KeyCode.Mouse0) && Time.time > nextAttack)
            {
                nextAttack = Time.time + SwingCd; attackUntil = Time.time + 0.6f;
                if (AimSlot != 255)
                {
                    var s = AimSlot;
                    NunMode.Send(NEv.Hit, w => { w.Write(s); w.Write(HitDamage); });
                    hitFlashUntil = Time.time + 0.5f;
                    var hit = GameObject.Find("Characters/Killer Stuff new/Audio Follow Killer/Hits/Hit Audio Source Random");
                    hit?.GetComponent<AudioSourceRandom>()?.PlayRandom();
                }
            }
            if (Down(KeyCode.T))
            {
                if (trapsLeft <= 0) Say("No traps left");
                else if (Time.time < cdTrap) Say($"Trap ready in {cdTrap - Time.time:0}s");
                else { trapsLeft--; cdTrap = Time.time + 8f; var y = yaw; NunMode.Send(NEv.Trap, w => { w.Write(pos.x); w.Write(pos.y); w.Write(pos.z); w.Write(y); }); Say($"Trap set ({trapsLeft} left)"); }
            }
            if (Down(KeyCode.Mouse1))
            {   // drag out whoever hides where I'm looking; guessing wrong costs a long cooldown
                if (Time.time < cdPull) Say($"Drag ready in {cdPull - Time.time:0}s");
                else
                {
                    var cam = Camera.main.transform;
                    var look = Physics.Raycast(cam.position, cam.forward, out var h, 3f) ? h.point : cam.position + cam.forward * 2f;
                    var hider = PlayerSync.Puppets.Values.FirstOrDefault(p => p && (p.Flags & PlayerFlags.Hiding) != 0 && Vector3.Distance(p.transform.position, look) < 2.5f);
                    if (hider) { var s = hider.Slot; cdPull = Time.time + PullCdHit; NunMode.Send(NEv.Pull, w => w.Write(s)); Say("Found you."); }
                    else { cdPull = Time.time + PullCdMiss; Say($"Nobody there. ({PullCdMiss:0}s)"); }
                }
            }
            if (Down(KeyCode.G))
            {
                if (Time.time < cdGas) Say($"Gas ready in {cdGas - Time.time:0}s");
                else { cdGas = Time.time + 60f; gasOffAt = Time.time + 15f; NunMode.Send(NEv.Gas, w => w.Write(true)); Say("Gas in the vents"); }
            }
            if (Down(KeyCode.R) && Time.time > cdScream) { cdScream = Time.time + 25f; NunMode.Send(NEv.Scream, w => { w.Write(pos.x); w.Write(pos.y); w.Write(pos.z); }); }
            if (Down(KeyCode.F))
            {
                if (Time.time < cdLights) Say($"Lights out ready in {cdLights - Time.time:0}s");
                else { cdLights = Time.time + 40f; NunMode.Send(NEv.Lights, w => { w.Write(pos.x); w.Write(pos.y); w.Write(pos.z); w.Write(15f); w.Write(10f); }); Say("Darkness"); }
            }
        }

        // A survivor got the jumpscare: she hears the sting too, and is told who.
        // Her "they saw you" cue: the stinger clip on our OWN 2D source, outside the game's "Loud" mixer group
        // (which evens loudness out, so the game's source ignored volume changes). Slider in the F2 panel.
        public static float NoticeVolume = PlayerPrefs.GetFloat("nunmp_noticevol", 0.15f);
        static AudioSource notice;
        public static void PlayNotice()
        {
            if (!notice)
            {
                var sting = GameObject.Find("Characters/Killer Stuff new/Audio Follow Killer/Chase/Stinger")?.GetComponent<AudioSource>()
                    ?? Resources.FindObjectsOfTypeAll<AudioSource>().FirstOrDefault(a => a.name == "Stinger" && a.clip);
                var go = new GameObject("NunMP Notice"); DontDestroyOnLoad(go);
                notice = go.AddComponent<AudioSource>();
                notice.clip = sting ? sting.clip : null; notice.spatialBlend = 0; notice.playOnAwake = false;
                notice.outputAudioMixerGroup = null;
            }
            if (!notice.clip) return;
            notice.volume = NoticeVolume;
            notice.Play();
        }

        // A survivor's revolver hit me (vanilla: she bleeds, flees, and is blind + deaf for 60 s; a player gets 25 s).
        public static void Shot()
        {
            if (!NunMode.IAmNun) return;
            shotUntil = Time.time + ShotStun; staggerUntil = Time.time + ShotStagger; shotFlashUntil = Time.time + 1.5f;
            pitch = Mathf.Clamp(pitch - 14f, -75, 75); // knocked back
            var scream = GameObject.Find("Characters/Killer Stuff new/Audio Follow Killer/Chase/Scream");
            var rnd = scream ? scream.GetComponent<AudioSourceRandom>() : null;
            if (rnd) rnd.PlayRandom(); else scream?.GetComponent<AudioSource>()?.Play();
            Say("SHOT!  Get away - no powers for a while", 3.5f);
            Plugin.Log.LogInfo("I was shot");
        }
        static float shotFlashUntil;
        static Texture2D red;

        public static void Scared(byte slot)
        {
            if (!NunMode.IAmNun) return;
            PlayNotice();
            var name = Session.Peers.TryGetValue(slot, out var p) ? p.Name : "Someone";
            Say($"{name} SAW YOU", 3);
            Plugin.Log.LogInfo($"jumpscare notice: {name} saw me");
        }

        // Closest survivor within range and cone in front of her eyes, with a clear line.
        static byte Aim(GameObject k, float range, float cone)
        {
            var cam = Camera.main;
            var eyePos = cam ? cam.transform.position : k.transform.position + Vector3.up * 1.5f;
            var fwd = Quaternion.Euler(pitch, yaw, 0) * Vector3.forward;
            Puppet best = null; float bestD = range;
            foreach (var p in PlayerSync.Puppets.Values)
            {
                if (!p || (p.Flags & (PlayerFlags.Dead | PlayerFlags.Hiding)) != 0) continue;
                var chest = p.transform.position + Vector3.up * 1.1f;
                var d = Vector3.Distance(eyePos, chest);
                if (d > bestD || Vector3.Angle(fwd, chest - eyePos) > cone) continue;
                if (Physics.Linecast(eyePos, chest, out var h, ~(1 << LayerMask.NameToLayer("Player")), QueryTriggerInteraction.Ignore) && !h.transform.IsChildOf(p.transform) && !h.transform.IsChildOf(k.transform)) continue;
                best = p; bestD = d;
            }
            return best ? best.Slot : (byte)255;
        }

        // "Has anyone seen me lately?" decides whether noise pings show (survivors report SeesNun in their state).
        void Sight(GameObject k)
        {
            if (PlayerSync.Puppets.Values.Any(p => p && (p.Flags & (PlayerFlags.SeesNun | PlayerFlags.Dead)) == PlayerFlags.SeesNun)) lastSeen = Time.time;
        }

        static void Say(string s, float secs = 2.5f) { msg = s; msgUntil = Time.time + secs; }

        static GUIStyle big;
        void OnGUI()
        {
            if (!NunMode.IAmNun) return;
            float w = Screen.width, h = Screen.height;
            if (big == null) big = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
            // crosshair + hit marker + swing cooldown under it
            var oldC = GUI.color;
            if (Time.time < shotFlashUntil)
            {
                if (!red) { red = new Texture2D(1, 1); red.SetPixel(0, 0, Color.white); red.Apply(); }
                GUI.color = new Color(0.7f, 0, 0, 0.55f * (shotFlashUntil - Time.time) / 1.5f);
                GUI.DrawTexture(new Rect(0, 0, w, h), red);
                GUI.color = oldC;
            }
            if (Time.time < hitFlashUntil) { GUI.color = Color.red; GUI.Label(new Rect(w / 2 - 60, h / 2 - 50, 120, 40), "HIT", big); }
            GUI.color = Time.time < hitFlashUntil ? Color.red : oldC;
            GUI.Label(new Rect(w / 2 - 6, h / 2 - 10, 20, 20), AimSlot != 255 ? "X" : "+");
            GUI.color = oldC;
            if (Time.time < nextAttack)
            {
                float left = nextAttack - Time.time;
                GUI.Box(new Rect(w / 2 - 30, h / 2 + 14, 60, 6), "");
                GUI.Box(new Rect(w / 2 - 30, h / 2 + 14, 60 * (1 - left / SwingCd), 6), "");
                GUI.Label(new Rect(w / 2 - 20, h / 2 + 20, 60, 20), $"{left:0.0}s");
            }
            GUI.Box(new Rect(20, h - 34, 200, 14), "");
            GUI.Box(new Rect(20, h - 34, 200 * Stamina, 14), "");
            GUI.Label(new Rect(22, h - 52, 300, 20), ShotLeft > 0 ? $"SHOT - powers back in {ShotLeft:0}s" : winded ? "WINDED" : Lurking ? "LURKING (they don't feel you)" : NunMode.Sneaking ? "SNEAKING (no static)" : NunMode.Sprinting && crouched ? "CROUCH-RUN (they feel you)" : NunMode.Sprinting ? "SPRINT" : "");
            string Cd(float t) => ShotLeft > 0 ? "locked" : Time.time >= t ? "ready" : $"{t - Time.time:0}s";
            GUI.Label(new Rect(w - 270, h - 155, 260, 150),
                $"LMB  swing  {(ShotLeft > 0 ? "locked" : Time.time >= nextAttack ? "ready" : $"{nextAttack - Time.time:0.0}s")}\nShift sprint   Ctrl sneak   Q/E peek\nRMB  drag out of hiding  {Cd(cdPull)}\nT  bear trap ({trapsLeft})  {Cd(cdTrap)}\nG  vent gas  {Cd(cdGas)}\nR  scream  {Cd(cdScream)}\nF  lights out  {Cd(cdLights)}");
            if (Time.time < msgUntil) GUI.Label(new Rect(w * 0.1f, h * 0.26f, w * 0.8f, 60), msg, big);
            // noise pings only when nobody has seen her for a while
            var cam = Camera.main;
            if (cam && PingsOn)
                foreach (var p in NunMode.RecentPings)
                {
                    var sp = cam.WorldToScreenPoint(p);
                    if (sp.z > 0) GUI.Label(new Rect(sp.x - 15, h - sp.y - 10, 40, 20), "( ! )");
                    else GUI.Label(new Rect(Vector3.Dot(p - cam.transform.position, cam.transform.right) > 0 ? w - 40 : 10, h / 2, 40, 20), "( ! )");
                }
        }
    }
}
