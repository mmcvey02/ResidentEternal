"""Reference RE2 camera -> Cities: Skylines camera mapping (protocol/PROTOCOL.md section 4).

The C# mod's PoseMapper must agree with this; protocol/vectors.json holds the shared test vectors.
Quaternions are (x, y, z, w).
"""
import math


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def qrot(q, v):
    """Rotate vector v by unit quaternion q."""
    x, y, z, w = q
    vx, vy, vz = v
    # t = 2 * cross(q.xyz, v)
    tx, ty, tz = 2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty),
            vy + w * ty + (z * tx - x * tz),
            vz + w * tz + (x * ty - y * tx))


def yaw_q(deg):
    """Rotation of deg degrees about +Y (Unity: clockwise seen from above)."""
    h = math.radians(deg) / 2
    return (0.0, math.sin(h), 0.0, math.cos(h))


def to_unity(p, q, flip_z=True):
    if not flip_z:
        return tuple(p), tuple(q)
    return (p[0], p[1], -p[2]), (-q[0], -q[1], q[2], q[3])


def map_pose(p, q, origin, anchor, heading_deg, scale=1.0, flip_z=True):
    """RE2 camera (p, q) -> city camera (position, rotation). origin is a raw RE2 position."""
    pu, qu = to_unity(p, q, flip_z)
    ou, _ = to_unity(origin, (0, 0, 0, 1), flip_z)
    d = tuple(scale * (pu[i] - ou[i]) for i in range(3))
    h = yaw_q(heading_deg)
    r = qrot(h, d)
    pos = tuple(anchor[i] + r[i] for i in range(3))
    rot = qmul(h, qu)
    return pos, rot


def forward(q):
    """Unity camera forward (+Z) for a rotation."""
    return qrot(q, (0.0, 0.0, 1.0))
