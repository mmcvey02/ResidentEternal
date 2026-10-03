using System;

namespace RaccoonCitySkylines
{
    public struct Vec3
    {
        public double X, Y, Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3 operator *(double s, Vec3 a) { return new Vec3(s * a.X, s * a.Y, s * a.Z); }
    }

    public struct Quat
    {
        public double X, Y, Z, W;

        public Quat(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static readonly Quat Identity = new Quat(0, 0, 0, 1);

        public static Quat operator *(Quat a, Quat b)
        {
            return new Quat(
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
        }

        public Vec3 Rotate(Vec3 v)
        {
            double tx = 2 * (Y * v.Z - Z * v.Y), ty = 2 * (Z * v.X - X * v.Z), tz = 2 * (X * v.Y - Y * v.X);
            return new Vec3(
                v.X + W * tx + (Y * tz - Z * ty),
                v.Y + W * ty + (Z * tx - X * tz),
                v.Z + W * tz + (X * ty - Y * tx));
        }

        /// <summary>Rotation of deg degrees about +Y (clockwise seen from above in Unity).</summary>
        public static Quat Yaw(double deg)
        {
            double h = deg * Math.PI / 360.0;
            return new Quat(0, Math.Sin(h), 0, Math.Cos(h));
        }

        public Quat Normalized()
        {
            double n = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
            return n < 1e-9 ? Identity : new Quat(X / n, Y / n, Z / n, W / n);
        }
    }

    /// <summary>
    /// RE2 camera -> Cities: Skylines camera (protocol/PROTOCOL.md section 4). Must agree with
    /// tools/rcsk/mapping.py; both are checked against protocol/vectors.json.
    /// </summary>
    public sealed class PoseMapper
    {
        public Vec3 Anchor;
        public double HeadingDeg;
        public double Scale = 1.0;
        public bool FlipZ = true;

        /// <summary>The raw RE2 position that lands on the anchor. Captured by <see cref="Recentre"/>.</summary>
        public Vec3 Origin;
        public bool HasOrigin;

        public void Recentre(Vec3 re2Position)
        {
            Origin = re2Position;
            HasOrigin = true;
        }

        public static void ToUnity(bool flipZ, ref Vec3 p, ref Quat q)
        {
            if (!flipZ)
                return;
            p = new Vec3(p.X, p.Y, -p.Z);
            q = new Quat(-q.X, -q.Y, q.Z, q.W);
        }

        public void Map(Vec3 re2Position, Quat re2Rotation, out Vec3 position, out Quat rotation)
        {
            if (!HasOrigin)
                Recentre(re2Position);
            Vec3 p = re2Position;
            Quat q = re2Rotation.Normalized();
            ToUnity(FlipZ, ref p, ref q);
            Vec3 o = Origin;
            Quat unused = Quat.Identity;
            ToUnity(FlipZ, ref o, ref unused);
            Quat h = Quat.Yaw(HeadingDeg);
            position = Anchor + h.Rotate(Scale * (p - o));
            rotation = h * q;
        }
    }
}
