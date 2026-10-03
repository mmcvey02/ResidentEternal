using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace RaccoonCitySkylines
{
    /// <summary>
    /// Drives Cities: Skylines' main camera from RE2's. The game's CameraController is switched off while the feed runs
    /// (the way first-person camera mods do it) and restored after.
    /// </summary>
    public sealed class CameraSync
    {
        public readonly PoseMapper Mapper = new PoseMapper();
        CameraController controller;
        Vector3 savedPosition;
        Quaternion savedRotation;
        float savedFov;
        bool driving;

        public long LastHostFrame { get; private set; }
        public bool Driving { get { return driving; } }

        /// <summary>Puts the anchor at the first police station, or at the configured point.</summary>
        public void ResolveAnchor(Settings s)
        {
            Vector3 anchor = new Vector3(s.AnchorX, 0f, s.AnchorZ);
            float heading = s.HeadingDeg;
            bool found = false;
            if (s.AnchorAtPoliceStation)
            {
                Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
                for (int i = 1; i < buffer.Length; i++)
                {
                    if ((buffer[i].m_flags & Building.Flags.Created) == Building.Flags.None)
                        continue;
                    BuildingInfo info = buffer[i].Info;
                    if (info == null || info.m_class == null || info.m_class.m_service != ItemClass.Service.PoliceDepartment)
                        continue;
                    anchor = buffer[i].m_position;
                    heading += buffer[i].m_angle * Mathf.Rad2Deg;
                    found = true;
                    break;
                }
            }
            if (!found)
                anchor.y = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(anchor);
            anchor.y += s.EyeHeight;
            Mapper.Anchor = new Vec3(anchor.x, anchor.y, anchor.z);
            Mapper.HeadingDeg = heading;
            Mapper.Scale = s.Scale;
            Mapper.FlipZ = s.FlipZ;
            Mapper.HasOrigin = false;
            Log.Info("anchor " + anchor + " heading " + heading + (found ? " (police station)" : " (manual)"));
        }

        public void Begin()
        {
            Camera cam = Camera.main;
            if (driving || cam == null)
                return;
            controller = cam.GetComponent<CameraController>();
            savedPosition = cam.transform.position;
            savedRotation = cam.transform.rotation;
            savedFov = cam.fieldOfView;
            if (controller != null)
                controller.enabled = false;
            driving = true;
        }

        public void End()
        {
            Camera cam = Camera.main;
            if (!driving)
                return;
            driving = false;
            if (cam != null)
            {
                cam.transform.position = savedPosition;
                cam.transform.rotation = savedRotation;
                cam.fieldOfView = savedFov;
            }
            if (controller != null)
                controller.enabled = true;
        }

        readonly double[] p = new double[3];
        readonly double[] q = new double[4];

        /// <summary>Applies a "cam" message. Call from LateUpdate so nothing moves the camera after it.</summary>
        public void Apply(Dictionary<string, object> cam)
        {
            Camera c = Camera.main;
            if (!driving || c == null || !Json.Nums(cam, "p", p) || !Json.Nums(cam, "q", q))
                return;
            Vec3 pos;
            Quat rot;
            Mapper.Map(new Vec3(p[0], p[1], p[2]), new Quat(q[0], q[1], q[2], q[3]), out pos, out rot);
            c.transform.position = new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z);
            c.transform.rotation = new Quaternion((float)rot.X, (float)rot.Y, (float)rot.Z, (float)rot.W);
            float fov = (float)Json.Num(cam, "fov", c.fieldOfView);
            if (fov > 5f && fov < 150f)
                c.fieldOfView = fov;
            LastHostFrame = (long)Json.Num(cam, "f", 0);
        }

        public void Recentre(Dictionary<string, object> cam)
        {
            if (cam != null && Json.Nums(cam, "p", p))
                Mapper.Recentre(new Vec3(p[0], p[1], p[2]));
        }
    }
}
