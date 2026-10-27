using System.Collections.Generic;
using System.IO;
using System.Linq;
using NunMP.Net;
using Steamworks;
using UnityEngine;
using UnityEngine.Audio;

namespace NunMP.Sync
{
    // Proximity voice: Opus (direct mic) or Steam codec, sent peer to peer to whoever is in earshot, played from each player's body in 3D through the game's own mixer,
    // muffled by walls between you and them, with reverb matched to the space they're standing in.
    public class Voice : MonoBehaviour
    {
        public const int Rate = 24000;
        const byte FlagDead = 1, FlagRawPcm = 2, FlagRadio = 4, FlagOpus = 8; // raw = dev tone; opus = DirectMic

        public static bool PushToTalk = PlayerPrefs.GetInt("nunmp_ptt", 1) == 1;
        public static float Volume = PlayerPrefs.GetFloat("nunmp_voicevol", 1f);
        public static KeyCode Key = KeyCode.V;
        public static bool Transmitting;
        public static float MyLevel;                   // 0..1 loudness of what I'm sending
        static readonly Dictionary<byte, Speaker> speakers = new Dictionary<byte, Speaker>();
        static readonly Dictionary<byte, Speaker> radioSpeakers = new Dictionary<byte, Speaker>();
        public static bool RadioTx, TestRadio, TestPTT;
        public static bool MicTest;                  // panel 'Test mic': capture + meter without a game
        public static EVoiceResult LastResult = EVoiceResult.k_EVoiceResultNotInitialized;
        public static float LastData = -99, KeyHeldSince = -1;
        public static EVoiceResult LastDecode; public static int LastPacket, LastPcm, Peak;
        static float nextRadioNoise;
        static readonly byte[] capBuf = new byte[8192], pcmBuf = new byte[Rate * 2 * 2];
        static bool recording;
        static float stopAt, nextNoise, hangover, pttTail;
        static readonly Queue<(byte[] data, int frames)> preroll = new Queue<(byte[], int)>(); static int prerollFrames;
        static float toneUntil;

        public static void SaveSettings() { PlayerPrefs.SetInt("nunmp_ptt", PushToTalk ? 1 : 0); PlayerPrefs.SetFloat("nunmp_voicevol", Volume); }

        public static void Init()
        {
            Session.On(Msg.Voice, (slot, r) =>
            {
                if (!GameFlow.InGame || slot == Session.MySlot) return;
                var flags = r.ReadByte();
                var data = r.ReadBytes((int)(r.BaseStream.Length - r.BaseStream.Position));
                // dead talk only reaches the dead (so they can't call out the nun to the living)
                if ((flags & FlagDead) != 0 && !LivesSync.Spectating) return;
                bool raw = (flags & FlagRawPcm) != 0, dead = (flags & FlagDead) != 0;
                if ((flags & FlagOpus) != 0) { data = DirectMic.Decode(slot, data); raw = true; } // decode once, both speakers get PCM
                GetSpeaker(slot)?.Feed(data, raw, dead);
                // walkie-talkie: if I carry one too, it plays through my radio (and crackles out loud)
                if ((flags & FlagRadio) != 0 && Radio.HasRadio && !LivesSync.Spectating && !dead)
                {
                    if (!radioSpeakers.TryGetValue(slot, out var rs) || !rs) radioSpeakers[slot] = rs = Speaker.Create(slot, radio: true);
                    rs.Feed(data, raw, false);
                    RadioNoise();
                }
            });
            Session.PeerLeft += slot =>
            {
                if (speakers.TryGetValue(slot, out var s) && s) Destroy(s.gameObject); speakers.Remove(slot);
                if (radioSpeakers.TryGetValue(slot, out var rs) && rs) Destroy(rs.gameObject); radioSpeakers.Remove(slot);
                DirectMic.Forget(slot);
            };
        }

        static Speaker GetSpeaker(byte slot)
        {
            if (speakers.TryGetValue(slot, out var s) && s) return s;
            s = Speaker.Create(slot);
            speakers[slot] = s;
            return s;
        }

        public static void ToneTest(float secs) => toneUntil = Time.unscaledTime + secs;

        void Update()
        {
            bool live = GameFlow.InGame || MicTest;
            if (!live || (!DirectMic.UseDirect && !SteamLobby.Ready)) // direct mic doesn't need Steam
            {
                if (recording) { SteamUser.StopVoiceRecording(); recording = false; }
                DirectMic.Stop();
                Transmitting = false;
                if (!GameFlow.InGame && speakers.Count + radioSpeakers.Count > 0)
                {
                    foreach (var s in speakers.Values.Concat(radioSpeakers.Values)) if (s) Destroy(s.gameObject);
                    speakers.Clear(); radioSpeakers.Clear();
                }
                return;
            }
            RadioTx = Radio.HasRadio && !LivesSync.Spectating && (Input.GetKey(Radio.Key) || TestRadio);
            bool keyHeld = Input.GetKey(Key) || RadioTx || TestPTT;
            if (keyHeld && KeyHeldSince < 0) KeyHeldSince = Time.unscaledTime; else if (!keyHeld) KeyHeldSince = -1;
            bool want = MicTest || !PushToTalk || keyHeld;
            if (DirectMic.UseDirect)
            {   // our own capture: boosted, and while push-to-talk is held nothing is dropped as "silence"
                if (recording) { SteamUser.StopVoiceRecording(); recording = false; }
                DirectMic.Start();
                var mu = DirectMic.Read(out var lvl, out var frames, keyHeld || MicTest);
                if (mu != null)
                {
                    MyLevel = Mathf.Max(lvl, MyLevel * 0.8f);
                    bool openMicVoice = lvl > 0.015f; if (openMicVoice) hangover = Time.unscaledTime + 0.5f;
                    if (keyHeld) pttTail = Time.unscaledTime + 0.35f; // keep sending a moment after V is released
                    bool send = GameFlow.InGame && (Time.unscaledTime < pttTail || (!PushToTalk && Time.unscaledTime < hangover));
                    if (send)
                    {
                        // first packet of a talk spurt: include the last ~250 ms before V went down (pre-roll)
                        if (preroll.Count > 0) { foreach (var pr in preroll) Send(pr.data, FlagOpus); preroll.Clear(); prerollFrames = 0; }
                        LastData = Time.unscaledTime; Send(mu, FlagOpus);
                    }
                    else
                    {   // ~250 ms = 12 Opus frames
                        preroll.Enqueue((mu, frames)); prerollFrames += frames;
                        while (prerollFrames > 12 && preroll.Count > 0) prerollFrames -= preroll.Dequeue().frames;
                    }
                }
                Transmitting = Time.unscaledTime - LastData < 0.3f;
                if (Time.unscaledTime < toneUntil && GameFlow.InGame) SendTone();
                NunHearsMe();
                return;
            }
            DirectMic.Stop();
            if (want && !recording) { SteamUser.StartVoiceRecording(); recording = true; }
            if (!want && recording && stopAt == 0) stopAt = Time.unscaledTime + 0.25f; // let Steam flush the tail
            if (want) stopAt = 0;
            if (recording && stopAt > 0 && Time.unscaledTime > stopAt) { SteamUser.StopVoiceRecording(); recording = false; stopAt = 0; }

            LastResult = SteamUser.GetAvailableVoice(out var avail);
            if (LastResult == EVoiceResult.k_EVoiceResultOK && avail > 0 &&
                (LastResult = SteamUser.GetVoice(true, capBuf, (uint)capBuf.Length, out var n)) == EVoiceResult.k_EVoiceResultOK && n > 0)
            {
                LastData = Time.unscaledTime;
                var pkt = new byte[n]; System.Array.Copy(capBuf, pkt, n);
                if (GameFlow.InGame) Send(pkt, 0);
                LastDecode = SteamUser.DecompressVoice(pkt, n, pcmBuf, (uint)pcmBuf.Length, out var pn, Rate);
                LastPacket = (int)n; LastPcm = (int)pn;
                if (LastDecode == EVoiceResult.k_EVoiceResultOK) { MyLevel = Rms(pcmBuf, (int)pn); Peak = 0; for (int i = 0; i + 1 < pn; i += 2) Peak = Mathf.Max(Peak, Mathf.Abs((short)(pcmBuf[i] | (pcmBuf[i + 1] << 8)))); }
            }
            else MyLevel = Mathf.MoveTowards(MyLevel, 0, Time.unscaledDeltaTime * 2);
            Transmitting = Time.unscaledTime - LastData < 0.3f; // Steam hands audio over in ~100 ms chunks

            if (Time.unscaledTime < toneUntil && GameFlow.InGame) SendTone();
            NunHearsMe();
        }

        static void Send(byte[] payload, byte extra)
        {
            byte flags = (byte)(extra | (LivesSync.Spectating ? FlagDead : 0) | (RadioTx ? FlagRadio : 0));
            // straight to each peer who could hear it (no host hop). reliable: over the internet a lost voice
            // packet is a hole in a word; a resend costs a few ms
            foreach (var p in Session.Peers.Values)
                if (p.Slot != Session.MySlot && ((flags & (FlagDead | FlagRadio)) != 0 || InEarshot(p.Slot)))
                {
                    Session.SendTo(p, Msg.Voice, w => { w.Write(flags); w.Write(payload); }, true);
                    SentBytes += payload.Length + 3; SentTo++;
                }
        }

        // Proximity culling: skip peers too far away to hear me (Speaker maxDistance 30 m, the nun's 40 m, + margin).
        // Unknown position or a dead/spectating listener (hears from their spectate camera) -> send anyway.
        public static long SentBytes, SentTo; // dev stats (bridge 'voice')
        public static bool InEarshot(byte slot)
        {
            var me = NunMode.IAmNun && NunSync.Killer ? NunSync.Killer.transform : PlayerSync.Local ? PlayerSync.Local.transform : null;
            Vector3? them = null;
            if (PlayerSync.Puppets.TryGetValue(slot, out var pz) && pz) { if ((pz.Flags & PlayerFlags.Dead) != 0) return true; them = pz.transform.position; }
            else if (NunMode.Active && slot == Rules.NunSlot && NunSync.Killer) them = NunSync.Killer.transform.position;
            if (!me || them == null) return true;
            return Vector3.Distance(me.position, them.Value) < (NunMode.IAmNun ? 45f : 35f);
        }

        // Dev: 440 Hz tone as raw PCM, ~50 ms per frame, to test playback without a mic.
        static float tonePhase;
        static void SendTone()
        {
            int samples = Mathf.RoundToInt(Rate * Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            var b = new byte[samples * 2];
            for (int i = 0; i < samples; i++)
            {
                short v = (short)(Mathf.Sin(tonePhase) * 8000); tonePhase += 2 * Mathf.PI * 440 / Rate;
                b[i * 2] = (byte)v; b[i * 2 + 1] = (byte)(v >> 8);
            }
            Transmitting = true; MyLevel = 0.5f;
            Send(b, FlagRawPcm);
        }

        // Human-readable Steam voice state for the panel / HUD.
        public static string Status
        {
            get
            {
                if (DirectMic.UseDirect) return DirectMic.Error.Length > 0 ? DirectMic.Error : "direct mic: " + (DirectMic.Device ?? "none");
                if (!SteamLobby.Ready) return "Steam not ready: " + SteamLobby.Error;
                switch (LastResult)
                {
                    case EVoiceResult.k_EVoiceResultOK: return "receiving audio";
                    case EVoiceResult.k_EVoiceResultNoData: return "recording, but silence - check Steam > Settings > Voice > input device";
                    case EVoiceResult.k_EVoiceResultNotRecording: return "not recording";
                    case EVoiceResult.k_EVoiceResultNotInitialized: return "idle";
                    default: return LastResult.ToString().Replace("k_EVoiceResult", "");
                }
            }
        }

        // A radio receiving chatter is noise where *I* am (the nun can hear walkies crackle).
        static void RadioNoise()
        {
            if (!Rules.NunHearsVoice || NunMode.IAmNun || Time.unscaledTime < nextRadioNoise || !PlayerSync.Local) return;
            nextRadioNoise = Time.unscaledTime + 0.6f;
            Message.Detectable.NoiseMadeEvent?.Invoke(PlayerSync.Local.transform.position, 7f);
        }

        // Talking is noise: the nun can hear you, louder = farther (host rule).
        static void NunHearsMe()
        {
            if (!Rules.NunHearsVoice || LivesSync.Spectating || MyLevel < 0.03f || Time.unscaledTime < nextNoise || !PlayerSync.Local) return;
            nextNoise = Time.unscaledTime + 0.5f;
            var radius = Mathf.Lerp(3f, 16f, Mathf.Clamp01(MyLevel * 4f));
            Message.Detectable.NoiseMadeEvent?.Invoke(PlayerSync.Local.transform.position, radius);
        }

        public static float Rms(byte[] pcm, int bytes)
        {
            int n = bytes / 2; if (n == 0) return 0;
            double sum = 0;
            for (int i = 0; i < n; i++) { float s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f; sum += s * s; }
            return Mathf.Sqrt((float)(sum / n));
        }

        void OnGUI()
        {
            if (!GameFlow.InGame) return;
            if (Transmitting) GUI.Label(new Rect(Screen.width - 160, Screen.height - 40, 150, 24), LivesSync.Spectating ? "MIC (dead chat)" : RadioTx ? "MIC + RADIO" : "MIC");
            else if (KeyHeldSince >= 0 && Time.unscaledTime - KeyHeldSince > 0.7f)
                GUI.Label(new Rect(Screen.width - 420, Screen.height - 40, 410, 24), "MIC: Steam gives no audio (" + Status + ")");
            if (Radio.HasRadio && !NunMode.IAmNun) GUI.Label(new Rect(Screen.width - 160, Screen.height - 60, 150, 24), "RADIO  (hold B)");
            var cam = Camera.main;
            if (!cam) return;
            foreach (var s in speakers.Values)
                if (s && s.Talking && !s.Dead)
                {
                    var sp = cam.WorldToScreenPoint(s.transform.position + Vector3.up * 0.5f);
                    if (sp.z > 0 && sp.z < 40) GUI.Label(new Rect(sp.x - 10, Screen.height - sp.y - 10, 40, 20), "))");
                }
        }

        public static string ShortStats => speakers.Count == 0 ? "nobody has talked yet" : string.Join("  ", speakers.Where(kv => kv.Value).Select(kv => $"{(Session.Peers.TryGetValue(kv.Key, out var p) ? p.Name : "#" + kv.Key)}: {kv.Value.Packets} pk, {kv.Value.Underruns} gaps, {kv.Value.Dropped * 1000 / Rate} ms dropped"));

        public static string Debug => string.Join(" ", speakers.Select(kv => kv.Value ? kv.Value.Describe : "")) + " | radio: " + string.Join(" ", radioSpeakers.Select(kv => kv.Value ? kv.Value.Describe : "")) + " " + Radio.Debug;
    }

    // One remote voice: a streaming 3D AudioSource on their head with occlusion + room reverb.
    public class Speaker : MonoBehaviour
    {
        public byte Slot;
        public bool Dead, Talking, IsRadio;
        AudioDistortionFilter dist; AudioEchoFilter echo; AudioHighPassFilter hp;
        bool demonOn;
        AudioSource src;
        AudioLowPassFilter lp;
        AudioReverbFilter rev;
        readonly float[] ring = new float[Voice.Rate * 2];
        int wr, rd, buffered;
        bool primed;
        float lastFeed, nextEnv;
        readonly object gate = new object();
        static AudioMixerGroup mixer;
        static readonly byte[] pcm = new byte[Voice.Rate * 2 * 2];
        string room = "-"; int walls;

        public static Speaker Create(byte slot, bool radio = false)
        {
            var go = new GameObject((radio ? "NunMP Radio " : "NunMP Voice ") + slot);
            DontDestroyOnLoad(go);
            var s = go.AddComponent<Speaker>();
            s.Slot = slot;
            s.src = go.AddComponent<AudioSource>();
            if (!mixer) mixer = Resources.FindObjectsOfTypeAll<AudioMixerGroup>().FirstOrDefault(g => g.name == "Foley");
            s.src.outputAudioMixerGroup = mixer;   // same mixer/snapshots as the game's own world sounds
            s.src.clip = AudioClip.Create("voice" + slot, Voice.Rate, 1, Voice.Rate, true, s.OnRead);
            s.src.loop = true;
            s.src.spatialBlend = 1f;
            s.src.rolloffMode = AudioRolloffMode.Logarithmic;
            s.src.minDistance = 1.5f; s.src.maxDistance = 30f;
            s.src.dopplerLevel = 0;
            s.lp = go.AddComponent<AudioLowPassFilter>(); s.lp.cutoffFrequency = 22000;
            s.rev = go.AddComponent<AudioReverbFilter>(); s.rev.reverbPreset = AudioReverbPreset.Off;
            s.IsRadio = radio;
            if (radio)
            {   // tinny handset: band-limited, crunchy, in your hand (2D)
                s.src.spatialBlend = 0;
                s.hp = go.AddComponent<AudioHighPassFilter>(); s.hp.cutoffFrequency = 650;
                s.lp.cutoffFrequency = 3000;
                s.dist = go.AddComponent<AudioDistortionFilter>(); s.dist.distortionLevel = 0.55f;
            }
            s.src.Play();
            return s;
        }

        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew(); // audio thread can't read Time
        long lastFeedTicks;
        int catchUp; bool wasPlaying; public int Underruns, Dropped, Packets;
        float agc = 1f, outSum; int outN; public float OutLevel; // OutLevel: what actually reaches the speaker (debug)

        public void Feed(byte[] data, bool raw, bool dead)
        {
            Dead = dead;
            int bytes;
            if (raw) { bytes = Mathf.Min(data.Length, pcm.Length); System.Array.Copy(data, pcm, bytes); }
            else if (SteamUser.DecompressVoice(data, (uint)data.Length, pcm, (uint)pcm.Length, out var n, Voice.Rate) == EVoiceResult.k_EVoiceResultOK) bytes = (int)n;
            else return;
            // auto gain: quiet mics (or a mic far from the mouth) get boosted towards normal talking level, up to 5x
            float rms = Voice.Rms(pcm, bytes);
            if (rms > 0.002f) agc = Mathf.Lerp(agc, Mathf.Clamp(0.12f / rms, 1f, 8f), 0.3f);
            float g = agc * Voice.Volume; // listener volume lives in the samples: AudioSource.volume caps at 100%
            lock (gate)
            {
                for (int i = 0; i + 1 < bytes; i += 2)
                {
                    { float v = (short)(pcm[i] | (pcm[i + 1] << 8)) / 32768f * g; ring[wr] = v / (1f + Mathf.Abs(v) * 0.3f) * 1.3f; } // soft limit
                    wr = (wr + 1) % ring.Length;
                    if (buffered < ring.Length) buffered++; else rd = (rd + 1) % ring.Length; // overflow: drop oldest
                }
                if (!primed && buffered > Voice.Rate / 5) primed = true; // 200 ms cushion for internet jitter
                // last resort only (> 1.2 s behind = something stalled for a long time): jump to 200 ms
                if (buffered > Voice.Rate * 12 / 10) { int drop = buffered - Voice.Rate / 5; rd = (rd + drop) % ring.Length; buffered -= drop; Dropped += drop; }
            }
            lastFeed = Time.unscaledTime; Packets++;
            lastFeedTicks = clock.ElapsedMilliseconds;
        }

        // Audio thread.
        readonly System.Random noise = new System.Random();
        bool wasAudible; int squelch;
        void OnRead(float[] data)
        {
            lock (gate)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    // play if primed, or flush what's left once the talker has gone quiet (end of a sentence, a short 'hi')
                    if (!primed && buffered > 0 && clock.ElapsedMilliseconds - lastFeedTicks > 120) primed = true;
                    bool have = primed && buffered > 0;
                    float x = 0;
                    if (have)
                    {
                        x = ring[rd]; rd = (rd + 1) % ring.Length; buffered--;
                        if (buffered > Voice.Rate * 4 / 10 && (++catchUp & 15) == 0 && buffered > 1) { x = (x + ring[rd]) * 0.5f; rd = (rd + 1) % ring.Length; buffered--; }
                    }
                    else
                    {
                        if (wasPlaying) Underruns++;
                        if (clock.ElapsedMilliseconds - lastFeedTicks > 400) primed = false; // only re-buffer after a real pause
                    }
                    wasPlaying = have;
                    if (demonOn) x = PitchShift(x);
                    if (IsRadio)
                    {
                        if (have != wasAudible) { squelch = Voice.Rate / 12; wasAudible = have; } // click + hiss on key up/down
                        float n = (float)(noise.NextDouble() * 2 - 1);
                        if (have) x += n * 0.035f;
                        if (squelch > 0) { x += n * 0.25f; squelch--; }
                    }
                    data[i] = x;
                    outSum += x * x; if (++outN >= Voice.Rate / 4) { OutLevel = Mathf.Sqrt(outSum / outN); outSum = 0; outN = 0; }
                }
            }
        }

        // Pitch down without slowing her down: two crossfaded taps sliding through a short delay line.
        const int W = 1600; const float Ratio = 0.68f;
        readonly float[] dl = new float[W * 2]; int dw; float dd;
        float PitchShift(float x)
        {
            dl[dw] = x;
            dd += 1f - Ratio; if (dd >= W) dd -= W;
            float d2 = dd + W / 2f; if (d2 >= W) d2 -= W;
            float y = Tap(dd) * (1 - Mathf.Abs(2 * dd / W - 1)) + Tap(d2) * (1 - Mathf.Abs(2 * d2 / W - 1));
            dw = (dw + 1) % dl.Length;
            return y * 1.4f;
        }
        float Tap(float delay)
        {
            float pos = dw - delay; if (pos < 0) pos += dl.Length;
            int i0 = (int)pos % dl.Length; float f = pos - (int)pos; int i1 = (i0 + 1) % dl.Length;
            return dl[i0] * (1 - f) + dl[i1] * f;
        }

        // The nun player's voice: deep, distorted, dark, a short echo. (Proximity still applies on top.)
        void Demon(bool on)
        {
            if (on == demonOn) return;
            demonOn = on;
            if (on)
            {
                if (!dist) dist = gameObject.AddComponent<AudioDistortionFilter>();
                dist.distortionLevel = 0.6f; dist.enabled = true;
                if (!echo) echo = gameObject.AddComponent<AudioEchoFilter>();
                echo.delay = 90; echo.decayRatio = 0.3f; echo.wetMix = 0.3f; echo.dryMix = 1f; echo.enabled = true;
                src.maxDistance = 40f;
            }
            else { if (dist) dist.enabled = false; if (echo) echo.enabled = false; }
        }

        void Update()
        {
            Talking = Time.unscaledTime - lastFeed < 0.3f;
            src.volume = 1f;
            if (IsRadio)
            {   // standing right next to them? you hear the real voice, not the handset
                var prox = PlayerSync.Puppets.TryGetValue(Slot, out var pz) && pz && Camera.main ? Vector3.Distance(Camera.main.transform.position, pz.transform.position) : 99f;
                src.volume = prox < 6f ? 0f : 0.9f;
                return;
            }
            Demon(NunMode.Active && Slot == Rules.NunSlot);
            if (Dead) { src.spatialBlend = 0; lp.cutoffFrequency = 22000; rev.reverbPreset = AudioReverbPreset.Off; return; } // ghost voices: in your head
            src.spatialBlend = 1;
            if (PlayerSync.Puppets.TryGetValue(Slot, out var p) && p) transform.position = p.transform.position + Vector3.up * 1.6f;
            else if (demonOn && NunSync.Killer) transform.position = NunSync.Killer.transform.position + Vector3.up * 1.7f; // the nun player speaks from her body
            if (Time.unscaledTime < nextEnv) return;
            nextEnv = Time.unscaledTime + 0.3f;
            Occlusion();
            Room(p);
        }

        static int envMask = -1;
        static int EnvMask
        {
            get
            {
                if (envMask == -1)
                {
                    envMask = 0;
                    foreach (var l in new[] { "Wall", "Ground", "Ceiling", "Door", "Furniture", "Default" }) { int i = LayerMask.NameToLayer(l); if (i >= 0) envMask |= 1 << i; }
                }
                return envMask;
            }
        }

        // Walls between you and them: each one muffles and quiets the voice.
        void Occlusion()
        {
            var cam = Camera.main;
            if (!cam) return;
            var from = cam.transform.position; var to = transform.position;
            walls = Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to), EnvMask, QueryTriggerInteraction.Ignore)
                .Count(h => !h.transform.IsChildOf(cam.transform.root));
            walls = Mathf.Min(walls, 3);
            lp.cutoffFrequency = demonOn ? Mathf.Min(3200, walls == 0 ? 3200 : walls == 1 ? 1500 : 800) : walls == 0 ? 22000 : walls == 1 ? 1800 : walls == 2 ? 900 : 500;
            src.volume = walls == 0 ? 1f : walls == 1 ? 0.7f : walls == 2 ? 0.45f : 0.3f;
        }

        // Probe the space around the speaker: outdoors, vent, corridor, small room, big hall.
        void Room(Puppet p)
        {
            var at = transform.position;
            if (p && (p.Flags & PlayerFlags.InVent) != 0) { Set(AudioReverbPreset.SewerPipe, "vent"); return; }
            bool roof = Physics.Raycast(at, Vector3.up, 25f, EnvMask, QueryTriggerInteraction.Ignore);
            if (!roof) { Set(AudioReverbPreset.Plain, "outside"); return; }
            var d = new List<float>();
            for (int i = 0; i < 8; i++)
            {
                var dir = Quaternion.Euler(0, i * 45, 0) * Vector3.forward;
                d.Add(Physics.Raycast(at, dir, out var h, 25f, EnvMask, QueryTriggerInteraction.Ignore) ? h.distance : 25f);
            }
            float avg = d.Average(), min = d.Min(), max = d.Max();
            if (min < 1.6f && max > 7f) Set(AudioReverbPreset.StoneCorridor, "corridor");
            else if (avg < 2.5f) Set(AudioReverbPreset.Bathroom, "small room");
            else if (avg < 6f) Set(AudioReverbPreset.Stoneroom, "room");
            else Set(AudioReverbPreset.Auditorium, "hall");
        }

        void Set(AudioReverbPreset preset, string name) { if (rev.reverbPreset != preset) rev.reverbPreset = preset; room = name; }

        public string Describe { get { lock (gate) return $"s{Slot}{(IsRadio ? "R" : "")}{(demonOn ? "(demon)" : "")}:{(Talking ? "talking" : "quiet")} buf={buffered * 1000 / Voice.Rate}ms pk={Packets} gaps={Underruns} dropped={Dropped * 1000 / Voice.Rate}ms gain={agc:0.0} out={OutLevel:0.000} walls={walls} room={room} lp={lp.cutoffFrequency:0}{(Dead ? " DEAD" : "")}"; } }
    }
}
