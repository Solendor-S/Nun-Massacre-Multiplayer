using System.Collections.Generic;
using System.IO;
using System.Linq;
using NunMP.Net;
using UnityEngine;

namespace NunMP.Sync
{
    // The nun's sounds live under "Audio Follow Killer" and are played by her FSM, which only runs on the host.
    // Host streams every source's (playing, clip, volume); clients mirror. 2D sources (jumpscare stinger, chase
    // music) are personal: only whoever she's after hears them, on any machine. 3D ones (screams) are heard by all.
    public static class NunAudio
    {
        static AudioSource[] srcs;
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

        static AudioSource[] Sources()
        {
            if (srcs != null && srcs.All(s => s)) return srcs;
            var root = GameObject.Find("Characters/Killer Stuff new/Audio Follow Killer");
            srcs = root ? root.GetComponentsInChildren<AudioSource>(true).OrderBy(s => Plugin.Path_(s.transform)).ToArray() : new AudioSource[0];
            return srcs;
        }

        // Jumpscare stinger + chase music (2D, under ".../Chase/"); footsteps etc. stay audible to everyone.
        static bool Personal(AudioSource s) => s.spatialBlend < 0.5f && s.transform.parent && s.transform.parent.name == "Chase";

        // Host, inside the Nun snapshot.
        public static void Write(BinaryWriter w)
        {
            var s = Sources();
            bool hostIsTarget = Targets.Current == Targets.LocalSlot;
            w.Write((byte)s.Length);
            foreach (var a in s)
            {
                w.Write((byte)(a.isPlaying ? 1 : 0));
                w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(a.volume) * 255));
                w.Write(a.isPlaying && a.clip ? a.clip.name : "");
                if (Personal(a) && !NunMode.Active) a.mute = !hostIsTarget; // she's after someone else: their chase music, not mine
            }
        }

        // Client.
        public static void Read(BinaryReader r, bool iAmTarget)
        {
            var s = Sources();
            int n = r.ReadByte();
            for (int i = 0; i < n; i++)
            {
                bool playing = r.ReadByte() == 1;
                float vol = r.ReadByte() / 255f;
                var clip = r.ReadString();
                if (i >= s.Length) continue; // ponytail: assumes identical hierarchy (same game build is enforced)
                var a = s[i];
                if (NunMode.Active && Personal(a)) continue; // survivors run their own chase music in Play-as-Nun
                a.volume = vol;
                if (Personal(a)) a.mute = !iAmTarget;
                if (playing && (!a.isPlaying || (a.clip ? a.clip.name : "") != clip))
                {
                    if (clip.Length > 0 && (!a.clip || a.clip.name != clip)) { var c = Clip(clip); if (c) a.clip = c; }
                    a.Play();
                }
                else if (!playing && a.isPlaying) a.Stop();
            }
        }

        static AudioClip Clip(string name)
        {
            if (clips.Count == 0) foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>()) clips[c.name] = c;
            clips.TryGetValue(name, out var clip);
            return clip;
        }

        public static void Reset() { srcs = null; }
    }
}
