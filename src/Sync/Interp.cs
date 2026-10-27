using System.Collections.Generic;
using UnityEngine;

namespace NunMP.Sync
{
    // Snapshot buffer rendered ~100 ms in the past so 20 Hz updates look smooth.
    public class Interp
    {
        public const float Delay = 0.1f;
        struct Snap { public float t; public Vector3 p; public float yaw; }
        readonly List<Snap> buf = new List<Snap>();
        public float LastRecv = -1;

        public void Push(Vector3 p, float yaw)
        {
            var now = Time.unscaledTime;
            // teleports (respawn, warps) snap instead of sliding through walls
            if (buf.Count > 0 && (buf[buf.Count - 1].p - p).sqrMagnitude > 25f) buf.Clear();
            buf.Add(new Snap { t = now, p = p, yaw = yaw });
            if (buf.Count > 20) buf.RemoveAt(0);
            LastRecv = now;
        }

        public bool Sample(out Vector3 p, out float yaw)
        {
            p = default; yaw = 0;
            if (buf.Count == 0) return false;
            var t = Time.unscaledTime - Delay;
            int i = buf.FindIndex(s => s.t >= t);
            if (i <= 0) { var s = i < 0 ? buf[buf.Count - 1] : buf[0]; p = s.p; yaw = s.yaw; return true; }
            var a = buf[i - 1]; var b = buf[i];
            var k = Mathf.InverseLerp(a.t, b.t, t);
            p = Vector3.Lerp(a.p, b.p, k);
            yaw = Mathf.LerpAngle(a.yaw, b.yaw, k);
            return true;
        }

        public void Clear() => buf.Clear();
    }
}
