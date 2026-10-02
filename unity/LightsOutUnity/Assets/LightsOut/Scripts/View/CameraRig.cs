using UnityEngine;

namespace LightsOut
{
    public enum CamMode { Cockpit, Chase, TopDown }

    public class CameraRig
    {
        public Camera Cam; public CamMode Mode = CamMode.Cockpit;
        Vector3 chasePos; bool hasChase; float roll, bounce, pitchLag;

        public CameraRig(Camera cam) { Cam = cam; Cam.nearClipPlane = .05f; Cam.farClipPlane = 6000f; }

        public void Follow(Car c, Track t, float dt, bool attract)
        {
            var mode = attract ? CamMode.Chase : Mode;
            float y = t.ElevAt(c.PF), v = Mathf.Sqrt(c.VX * c.VX + c.VY * c.VY);
            Vector3 fwd = new Vector3(Mathf.Cos(c.H), 0, -Mathf.Sin(c.H));
            if (mode == CamMode.Cockpit)
            {
                float yawRate = c.Steer * Mathf.Min(Config.YawMax, Config.LatGrip(Mathf.Abs(c.VF), false) / Mathf.Max(Mathf.Abs(c.VF), 6)) * Mathf.Min(1, Mathf.Abs(c.VF) / 3);
                roll = Mathf.Lerp(roll, Mathf.Clamp(c.VF * yawRate / 45f, -1, 1) * 2f, dt * 6);
                bounce = Mathf.Lerp(bounce, -Mathf.Clamp(t.KV[c.Idx] * v * v * .004f, -.07f, .07f), dt * 8);
                pitchLag = Mathf.Lerp(pitchLag, c.Brk * 1.0f - c.Thr * .35f, dt * 5);
                float grade = Mathf.Atan(t.GR[c.Idx] * Mathf.Cos(c.H - t.Heading(c.Idx))) * Mathf.Rad2Deg;
                float shake = (Random.value - .5f) * .006f * Mathf.Min(1, v / 80) * (c.Surf > 0 ? 3 : 1);
                Cam.transform.position = Visuals.World(c.X, c.Y, y + 1.1f + bounce + shake) - fwd * .1f;
                Cam.transform.rotation = Visuals.Yaw(c.H) * Quaternion.Euler(2.6f - grade * .9f + pitchLag, 0, -roll);
                Cam.fieldOfView = 74 + Mathf.Min(12, v * .13f);
            }
            else if (mode == CamMode.Chase)
            {
                Vector3 target = Visuals.World(c.X, c.Y, y + 2.6f) - fwd * 8f;
                if (!hasChase || (chasePos - target).sqrMagnitude > 1600) { chasePos = target; hasChase = true; }
                chasePos = Vector3.Lerp(chasePos, target, Mathf.Min(1, dt * 7));
                Cam.transform.position = chasePos;
                Cam.transform.LookAt(Visuals.World(c.X, c.Y, y + 1f) + fwd * 6f);
                Cam.fieldOfView = 62 + Mathf.Min(10, v * .1f);
            }
            else
            {
                Cam.transform.position = Visuals.World(c.X, c.Y, y + 120 + v * .9f) + fwd * Mathf.Min(40, v * .38f);
                Cam.transform.rotation = Quaternion.Euler(90, 90 + c.H * Mathf.Rad2Deg, 0);
                Cam.fieldOfView = 50;
            }
        }
    }
}
