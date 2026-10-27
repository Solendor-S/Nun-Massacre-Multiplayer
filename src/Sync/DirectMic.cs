using System.Collections.Generic;
using Concentus.Enums;
using Concentus.Structs;
using UnityEngine;

namespace NunMP.Sync
{
    // Our own mic capture (Unity Microphone) instead of Steam's: we choose the gain and never drop quiet speech
    // as "silence" while push-to-talk is held. Sent as 24 kHz mono Opus, 20 ms frames (~24 kbit/s while talking).
    public static class DirectMic
    {
        public static bool UseDirect = PlayerPrefs.GetInt("nunmp_directmic", 1) == 1;
        public static float Boost = PlayerPrefs.GetFloat("nunmp_micboost", 3f);
        public static bool AutoBoost = PlayerPrefs.GetInt("nunmp_autoboost", 1) == 1;
        public static float AutoGain = 8f;          // learned while talking (auto boost)
        static float speechRms = 0.01f;
        public static int DeviceIndex = PlayerPrefs.GetInt("nunmp_micdev", 0);
        public static string Device => Microphone.devices.Length == 0 ? null : Microphone.devices[Mathf.Clamp(DeviceIndex, 0, Microphone.devices.Length - 1)];
        public static string Error = "";

        public const int Frame = Voice.Rate / 50;   // 20 ms
        const int Bitrate = 24000;
        static OpusEncoder enc;
        static readonly short[] frame = new short[Frame]; static int framed;
        static readonly byte[] opusOut = new byte[255];

        public static float ToneUntil; static float tonePh;
        static AudioClip clip;
        static string clipDevice;
        static int readPos;
        static float[] tmp = new float[Voice.Rate];

        public static void Save() { PlayerPrefs.SetInt("nunmp_directmic", UseDirect ? 1 : 0); PlayerPrefs.SetFloat("nunmp_micboost", Boost); PlayerPrefs.SetInt("nunmp_micdev", DeviceIndex); PlayerPrefs.SetInt("nunmp_autoboost", AutoBoost ? 1 : 0); }

        public static void Start()
        {
            if (clip && clipDevice == Device && Microphone.IsRecording(clipDevice)) return;
            Stop();
            if (Microphone.devices.Length == 0) { Error = "no microphone found"; return; }
            clipDevice = Device;
            clip = Microphone.Start(clipDevice, true, 1, Voice.Rate);
            readPos = 0; framed = 0; Error = clip ? "" : "could not open " + clipDevice;
        }

        public static void Stop()
        {
            if (clipDevice != null && Microphone.IsRecording(clipDevice)) Microphone.End(clipDevice);
            clip = null; clipDevice = null;
        }

        // New mic audio since last call, boosted + soft-limited, Opus-encoded: payload = [len byte][frame]... for each
        // complete 20 ms frame (null if none yet). level = RMS after boost.
        public static byte[] Read(out float level, out int frames, bool talking = false)
        {
            level = 0; frames = 0;
            if (!clip) return null;
            int pos = Microphone.GetPosition(clipDevice);
            int n = (pos - readPos + clip.samples) % clip.samples;
            if (n <= 0) return null;
            if (tmp.Length < n) tmp = new float[n];
            // ring read (the clip loops every second)
            if (readPos + n <= clip.samples) clip.GetData(tmp, readPos);
            else
            {
                var a = new float[clip.samples - readPos]; clip.GetData(a, readPos);
                var b = new float[n - a.Length]; clip.GetData(b, 0);
                a.CopyTo(tmp, 0); b.CopyTo(tmp, a.Length);
            }
            readPos = pos;
            if (Time.unscaledTime < ToneUntil) for (int i = 0; i < n; i++) { tmp[i] = Mathf.Sin(tonePh) * 0.02f; tonePh += 2 * Mathf.PI * 440 / Voice.Rate; } // dev: bridge 'opustone'
            // auto boost: learn how loud this mic is while talking, aim for normal speech level (~0.12 RMS)
            if (AutoBoost && talking)
            {
                double raw = 0; for (int i = 0; i < n; i++) raw += tmp[i] * tmp[i];
                float r = Mathf.Sqrt((float)(raw / n));
                if (r > 0.0015f) { speechRms = Mathf.Lerp(speechRms, r, r > speechRms ? 0.25f : 0.03f); AutoGain = Mathf.Clamp(0.12f / speechRms, 1f, 40f); }
            }
            if (enc == null)
            {
                enc = OpusEncoder.Create(Voice.Rate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
                enc.Bitrate = Bitrate; enc.UseVBR = true; enc.SignalType = OpusSignal.OPUS_SIGNAL_VOICE; enc.Complexity = 5;
            }
            float gain = AutoBoost ? AutoGain : Boost;
            double sum = 0;
            List<byte> outb = null;
            for (int i = 0; i < n; i++)
            {
                float x = tmp[i] * gain;
                x = x / (1f + Mathf.Abs(x) * 0.35f) * 1.35f; // soft limiter: loud stays loud, never harsh-clips
                x = Mathf.Clamp(x, -1f, 1f);
                sum += x * x;
                frame[framed++] = (short)(x * 32767);
                if (framed < Frame) continue;
                framed = 0;
                int len = enc.Encode(frame, 0, Frame, opusOut, 0, opusOut.Length);
                if (len <= 0) continue;
                if (outb == null) outb = new List<byte>(128);
                outb.Add((byte)len); for (int j = 0; j < len; j++) outb.Add(opusOut[j]);
                frames++;
            }
            level = Mathf.Sqrt((float)(sum / n));
            return outb?.ToArray();
        }

        // Receiver side: one decoder per talker (Opus is stateful). Decoded at 48 kHz and halved: Concentus 1.2's
        // 24 kHz decoder outputs silence (verified offline; its 48 kHz decoder round-trips our 24 kHz stream fine).
        static readonly Dictionary<byte, OpusDecoder> decoders = new Dictionary<byte, OpusDecoder>();
        static readonly short[] dec = new short[Frame * 2];

        public static byte[] Decode(byte slot, byte[] payload)
        {
            if (!decoders.TryGetValue(slot, out var d)) decoders[slot] = d = OpusDecoder.Create(Voice.Rate * 2, 1);
            var pcm = new List<byte>(payload.Length * 16);
            for (int i = 0; i < payload.Length;)
            {
                int len = payload[i++];
                if (len == 0 || i + len > payload.Length) break;
                int got;
                try { got = d.Decode(payload, i, len, dec, 0, Frame * 2); } catch (System.Exception) { got = 0; }
                i += len;
                for (int k = 0; k + 1 < got; k += 2) { var v = (short)((dec[k] + dec[k + 1]) / 2); pcm.Add((byte)v); pcm.Add((byte)(v >> 8)); }
            }
            return pcm.ToArray();
        }

        public static void Forget(byte slot) => decoders.Remove(slot);
    }
}
