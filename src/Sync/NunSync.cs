using NodeCanvas.StateMachines;
using NunMP.Net;
using UnityEngine;
using UnityEngine.AI;

namespace NunMP.Sync
{
    // Host runs the real nun AI and streams her; clients turn her into a puppet.
    public class NunSync : MonoBehaviour
    {
        public static GameObject Killer;
        public static string HostState = "";
        public static byte TargetSlot; // who the host's nun is after (0 = host)
        static readonly Interp interp = new Interp();
        static float lean;
        static float fwd, turn; static bool crouch, attack, active = true;
        Animator anim;
        bool puppeted;
        float nextSend;

        public static void Init()
        {
            Session.On(Msg.Nun, (slot, r) =>
            {
                if (slot != Driver || Session.MySlot == Driver) return;
                var p = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var yaw = r.ReadSingle();
                fwd = r.ReadSingle(); turn = r.ReadSingle();
                var b = r.ReadByte();
                crouch = (b & 1) != 0; attack = (b & 2) != 0; active = (b & 4) != 0;
                NunMode.Sneaking = (b & 8) != 0; NunMode.Sprinting = (b & 16) != 0;
                HostState = r.ReadString();
                TargetSlot = r.ReadByte();
                NunAudio.Read(r, TargetSlot == Session.MySlot);
                lean = r.ReadSByte() / 100f;
                interp.Push(p, yaw);
            });
        }

        public static byte Driver => NunMode.Active ? Rules.NunSlot : (byte)0;

        static FSMOwner Fsm => Killer ? Killer.GetComponent<FSMOwner>() : null;

        void Update()
        {
            if (!GameFlow.InGame) { Killer = null; puppeted = false; interp.Clear(); NunAudio.Reset(); return; }
            if (!Killer) { Killer = GameObject.Find("Characters/Killer Stuff new/Killer"); if (!Killer) return; anim = Killer.GetComponent<Animator>(); }
            // Whoever drives her streams her: the host's AI normally, the nun player in Play-as-Nun.
            if (Session.MySlot == Driver) { if (NunMode.Active) StopAI(keepAgent: true); HostSend(); }
            else ClientDrive();
        }

        void HostSend()
        {
            if (Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + 0.05f;
            var t = Killer.transform;
            var state = Fsm && Fsm.graph is FSM f && f.currentState != null ? f.currentStateName : "";
            HostState = state;
            Session.Broadcast(Msg.Nun, w =>
            {
                w.Write(t.position.x); w.Write(t.position.y); w.Write(t.position.z);
                w.Write(t.eulerAngles.y);
                w.Write(anim.GetFloat("Forward")); w.Write(anim.GetFloat("Turn"));
                w.Write((byte)((anim.GetBool("Crouch") ? 1 : 0) | (anim.GetBool("Attack") ? 2 : 0) | (Killer.activeInHierarchy ? 4 : 0) | (NunMode.Sneaking ? 8 : 0) | (NunMode.Sprinting ? 16 : 0)));
                w.Write(state ?? "");
                w.Write(NunMode.Active ? NunPlayer.AimSlot : Targets.Current == Targets.LocalSlot ? (byte)0 : Targets.Current);
                NunAudio.Write(w);
                w.Write((sbyte)Mathf.RoundToInt(NunPlayer.Lean * 100));
            }, false);
        }

        // AI off: FSM + movement brains. The nun player keeps her NavMeshAgent to walk on the navmesh.
        void StopAI(bool keepAgent)
        {
            if (puppeted) return;
            var fsm = Fsm;
            if (fsm) { fsm.StopBehaviour(); fsm.enabled = false; }
            var nav = Killer.GetComponent<NavMeshAgent>(); if (nav && !keepAgent) nav.enabled = false;
            var rb = Killer.GetComponent<Rigidbody>(); if (rb) rb.isKinematic = true;
            foreach (var mb in Killer.GetComponents<MonoBehaviour>())
                if (mb is Humanoid.Motor2d || mb is Humanoid.Agent.NavigationController || mb is Humanoid.Agent.aAnimationController) mb.enabled = false;
            anim.applyRootMotion = false;
            puppeted = true;
            Plugin.Log.LogInfo(keepAgent ? "nun AI off (I drive her)" : "nun puppeted");
        }

        // Her body leans when the nun player peeks: tilt spine + chest sideways after animation.
        void LateUpdate()
        {
            if (!GameFlow.InGame || !Killer || !anim || !anim.isHuman || NunMode.IAmNun || Mathf.Abs(lean) < 0.01f) return;
            var fwdAxis = Killer.transform.forward;
            foreach (var b in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest })
            {
                var t = anim.GetBoneTransform(b);
                if (t) t.rotation = Quaternion.AngleAxis(-14f * lean, fwdAxis) * t.rotation;
            }
        }

        void ClientDrive()
        {
            StopAI(keepAgent: false);
            if (!interp.Sample(out var p, out var yaw)) return;
            Killer.transform.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
            anim.SetFloat("Forward", fwd); anim.SetFloat("Turn", turn);
            anim.SetBool("Crouch", crouch); anim.SetBool("Attack", attack);
            anim.SetBool("OnGround", true);
        }
    }
}
