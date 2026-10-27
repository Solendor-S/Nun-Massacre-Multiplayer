using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using NunMP.Net;
using UnityEngine;

namespace NunMP.Sync
{
    public struct PlayerFlags
    {
        public const byte Crouch = 1, Hiding = 2, Light = 4, Dead = 8, InVent = 16, SeesNun = 32, Lighter = 64;
    }

    // Local player -> 20 Hz state. Remote players -> Puppet bodies.
    public class PlayerSync : MonoBehaviour
    {
        public static readonly Dictionary<byte, Puppet> Puppets = new Dictionary<byte, Puppet>();
        public static pReferences Local;
        static bool flashOn, lighterOn, inVent;
        float nextSend;

        public static void Init()
        {
            Session.On(Msg.Player, (slot, r) =>
            {
                if (slot == Session.MySlot || !GameFlow.InGame) return;
                var p = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                float yaw = r.ReadSingle(), pitch = r.ReadSingle();
                var flags = r.ReadByte();
                Get(slot)?.Push(p, yaw, pitch, flags);
            });
            Session.PeerLeft += slot => Remove(slot);
            MessagesGranny.FlashlightToggleEvent += on => flashOn = on;
            MessagesGranny.LighterToggleEvent += on => lighterOn = on;
            MessagesGranny.VentEnterExitEvent += v => inVent = v;
        }

        static Puppet Get(byte slot)
        {
            if (NunMode.Active && slot == Rules.NunSlot) return null; // she's the nun, not a survivor body
            if (Puppets.TryGetValue(slot, out var pp) && pp) return pp;
            pp = Puppet.Create(slot);
            if (pp) Puppets[slot] = pp;
            return pp;
        }

        static void Remove(byte slot)
        {
            if (Puppets.TryGetValue(slot, out var pp) && pp) Destroy(pp.gameObject);
            Puppets.Remove(slot);
        }

        public static void Clear()
        {
            foreach (var s in Puppets.Keys.ToList()) Remove(s);
            Local = null;
        }

        void Update()
        {
            if (!GameFlow.InGame) { if (Puppets.Count > 0 || Local) Clear(); return; }
            if (!Local) { var go = GameObject.FindGameObjectWithTag("Player"); Local = go ? go.GetComponent<pReferences>() : null; if (!Local) return; flashOn = lighterOn = false; inVent = false; }
            if (Time.unscaledTime < nextSend || NunMode.IAmNun) return;
            nextSend = Time.unscaledTime + 0.05f;
            var t = Local.transform;
            var cc = Local.characterController;
            var feet = cc ? t.position + cc.center - Vector3.up * (cc.height * 0.5f) : t.position;
            float pitch = Local.camera ? Local.camera.transform.eulerAngles.x : 0;
            byte f = 0;
            if (Local.firstPersonController && Local.firstPersonController.CheckIfPlayerCrouching()) f |= PlayerFlags.Crouch;
            if (Local.playerStates && Local.playerStates.StateCheck() == PlayerStates.States.Hiding) f |= PlayerFlags.Hiding;
            if (flashOn) f |= PlayerFlags.Light;
            if (lighterOn) f |= PlayerFlags.Lighter;
            if (inVent) f |= PlayerFlags.InVent;
            if (Local.health && !Local.health.isAlive()) f |= PlayerFlags.Dead;
            if (SeesNun()) f |= PlayerFlags.SeesNun;
            Session.Broadcast(Msg.Player, w =>
            {
                w.Write(feet.x); w.Write(feet.y); w.Write(feet.z);
                w.Write(t.eulerAngles.y); w.Write(pitch); w.Write(f);
            }, false);
        }

        // Client-side stand-in for the nun's IsOnCamera check (host can't see through our camera).
        static bool SeesNun()
        {
            var k = NunSync.Killer;
            var cam = Local.camera;
            if (!k || !cam || !k.activeInHierarchy) return false;
            var chest = k.transform.position + Vector3.up * 1.4f;
            var vp = cam.WorldToViewportPoint(chest);
            if (vp.z <= 0 || vp.z > 40 || vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1) return false;
            int mask = ~(1 << LayerMask.NameToLayer("Player"));
            return !Physics.Linecast(cam.transform.position, chest, out var hit, mask, QueryTriggerInteraction.Ignore) || hit.transform.IsChildOf(k.transform);
        }

    }

    public class Puppet : MonoBehaviour
    {
        public byte Slot;
        public byte Flags;
        public Dummy Dummy; // host only: what the nun sees/hits
        public float Pitch;
        public readonly Interp Interp = new Interp();
        Animator anim;
        Light lamp;
        Vector3 lastPos;
        float lastYaw;

        // Body = clone of an unused humanoid model in the scene (killer skin "Hillbilly2"), AI/physics stripped.
        // ponytail: borrowed model; swap for a proper survivor model if we ever make one.
        public static Puppet Create(byte slot)
        {
            var killer = GameObject.Find("Characters/Killer Stuff new/Killer");
            var template = killer ? killer.transform.Find("Model/Hillbilly2") : null;
            if (!template) { Plugin.Log.LogWarning("puppet template missing"); return null; }
            var root = new GameObject("NunMP Puppet " + slot);
            var ctrl = killer.GetComponent<Animator>().runtimeAnimatorController;
            // Preferred: PDM survivor (if extracted from the player's own Power Drill Massacre); else the hillbilly skin.
            var body = PdmBody.Create(ctrl, template.GetComponentInChildren<SkinnedMeshRenderer>(true)?.sharedMaterial);
            if (body) body.transform.SetParent(root.transform, false);
            else
            {
                body = Instantiate(template.gameObject, root.transform);
                foreach (var c in body.GetComponentsInChildren<Component>(true))
                    if (c is MonoBehaviour && !(c is Animator)) Destroy(c);
                    else if (c is Collider col) Destroy(col);
                    else if (c is Rigidbody rb) Destroy(rb);
                body.SetActive(true);
            }
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            var a = body.GetComponent<Animator>() ?? body.AddComponent<Animator>();
            a.runtimeAnimatorController = ctrl;
            a.applyRootMotion = false;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var lampGo = new GameObject("lamp");
            lampGo.transform.SetParent(root.transform, false);
            lampGo.transform.localPosition = new Vector3(0.2f, 1.4f, 0.3f);
            var l = lampGo.AddComponent<Light>();
            l.type = LightType.Spot; l.range = 15; l.spotAngle = 50; l.intensity = 1.2f; l.enabled = false;
            var p = root.AddComponent<Puppet>();
            p.Slot = slot; p.anim = a; p.lamp = l;
            if (Session.IsHost) p.Dummy = Dummy.Attach(p);
            Plugin.Log.LogInfo("puppet created for slot " + slot);
            return p;
        }

        public void Push(Vector3 pos, float yaw, float pitch, byte flags)
        {
            Interp.Push(pos, yaw);
            Pitch = pitch; Flags = flags;
        }

        // Caught: collapse to the floor with a blood burst, stay down through their death scene.
        float deadUntil; Quaternion deadRot; Vector3 deadPos;
        public void PlayDeath()
        {
            deadUntil = Time.time + 4.5f;
            deadPos = transform.position; deadRot = transform.rotation;
            anim.enabled = false;
            var blood = Resources.FindObjectsOfTypeAll<ParticleSystem>().FirstOrDefault(p => p.name.StartsWith("PS_Blood_Burst_Omni") && p.gameObject.scene.IsValid());
            if (blood)
            {
                var b = Instantiate(blood.gameObject, transform.position + Vector3.up * 1.1f, Quaternion.identity);
                b.SetActive(true); b.GetComponent<ParticleSystem>().Play(true); Destroy(b, 4f);
            }
        }

        void Update()
        {
            if (Time.time < deadUntil)
            {   // fall backwards over ~0.4 s, pivoting at the feet
                float k = Mathf.Clamp01((4.5f - (deadUntil - Time.time)) / 0.4f);
                transform.SetPositionAndRotation(deadPos + Vector3.up * 0.15f * k, deadRot * Quaternion.Euler(-88f * k * k, 0, 0));
                return;
            }
            if (!anim.enabled) anim.enabled = true;
            if (!Interp.Sample(out var pos, out var yaw)) return;
            var dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var vel = (pos - lastPos) / dt; vel.y = 0;
            var turn = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastPos = pos; lastYaw = yaw;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            bool hidden = (Flags & (PlayerFlags.Hiding | PlayerFlags.Dead)) != 0;
            anim.gameObject.SetActive(!hidden);
            if (hidden) return;
            // Forward 0.5 = walk, 1 = run in the standard third-person controller.
            anim.SetFloat("Forward", Mathf.Clamp01(vel.magnitude / 5f), 0.1f, Time.deltaTime);
            anim.SetFloat("Turn", Mathf.Clamp(turn / 360f, -1, 1), 0.1f, Time.deltaTime);
            anim.SetBool("Crouch", (Flags & PlayerFlags.Crouch) != 0);
            anim.SetBool("OnGround", true);
            // flashlight = beam from the chest; lighter = small warm bubble at the hand
            bool flash = (Flags & PlayerFlags.Light) != 0, lighter = (Flags & PlayerFlags.Lighter) != 0;
            lamp.enabled = flash || lighter;
            if (flash) { lamp.type = LightType.Spot; lamp.range = 15; lamp.spotAngle = 50; lamp.intensity = 1.2f; lamp.color = Color.white; lamp.transform.localPosition = new Vector3(0.2f, 1.4f, 0.3f); lamp.transform.localRotation = Quaternion.Euler(Pitch, 0, 0); }
            else if (lighter) { lamp.type = LightType.Point; lamp.range = 3.5f; lamp.intensity = 0.9f; lamp.color = new Color(1f, 0.7f, 0.35f); lamp.transform.localPosition = new Vector3(0.25f, 1.2f, 0.35f); }
        }
    }
}
