using System.IO;
using UnityEngine;

namespace NunMP.Sync
{
    // Host-chosen match rules; sent with Msg.Start so every peer plays by the host's settings.
    public static class Rules
    {
        public enum LivesMode : byte { Shared, PerPlayer }
        public enum OutMode : byte { Spectate, GameOverAll }

        public static int Lives;                 // 0 = game default (3, or 1 on Extreme)
        public static LivesMode Mode = LivesMode.Shared;
        public static OutMode Out = OutMode.Spectate;
        public static bool NunHearsVoice = true;
        public static byte NunSlot = 255;        // Play as Nun: who plays her (255 = random at start)
        public static byte NunSkin = 255;        // Play as Nun: SkinID the nun player picked (255 = host's menu choice)
        public static readonly (SkinID id, string label)[] Skins = { (SkinID.DefaultNun, "Nun"), (SkinID.Clown, "Clown"), (SkinID.Backrooms, "Backrooms"), (SkinID.PDM, "Driller") };

        public static void Write(BinaryWriter w) { w.Write((byte)Lives); w.Write((byte)Mode); w.Write((byte)Out); w.Write(NunHearsVoice); w.Write(NunSlot); w.Write(NunSkin); }
        public static void Read(BinaryReader r) { Lives = r.ReadByte(); Mode = (LivesMode)r.ReadByte(); Out = (OutMode)r.ReadByte(); NunHearsVoice = r.ReadBoolean(); NunSlot = r.ReadByte(); NunSkin = r.ReadByte(); }

        public static string Describe =>
            $"{(Lives == 0 ? "default" : Lives.ToString())} lives, {(Mode == LivesMode.Shared ? "shared pool" : "each player")}, out = {(Out == OutMode.Spectate ? "spectate" : "game over for all")}, nun {(NunHearsVoice ? "hears" : "ignores")} voices, nun player {(NunSlot == 255 ? "random" : NunSlot.ToString())}";

        // Host panel section.
        public static void DrawEditor(bool editable)
        {
            GUILayout.Label("RULES");
            GUI.enabled = editable;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Lives: " + (Lives == 0 ? "game default" : Lives.ToString()), GUILayout.Width(150));
            Lives = Mathf.RoundToInt(GUILayout.HorizontalSlider(Lives, 0, 9));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(Mode == LivesMode.Shared, "Shared pool")) Mode = LivesMode.Shared;
            if (GUILayout.Toggle(Mode == LivesMode.PerPlayer, "Each player")) Mode = LivesMode.PerPlayer;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Out of lives:", GUILayout.Width(90));
            if (GUILayout.Toggle(Out == OutMode.Spectate, "Spectate")) Out = OutMode.Spectate;
            if (GUILayout.Toggle(Out == OutMode.GameOverAll, "Game over for all")) Out = OutMode.GameOverAll;
            GUILayout.EndHorizontal();
            NunHearsVoice = GUILayout.Toggle(NunHearsVoice, "The nun can hear you talk");
            GUI.enabled = true;
        }
    }
}
