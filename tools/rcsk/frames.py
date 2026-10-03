"""Frames over named shared memory (Windows) or /dev/shm files (elsewhere). Layout: protocol/PROTOCOL.md section 2."""
import mmap
import os
import struct
import sys

MAGIC = 0x4B534352  # "RCSK"
VERSION = 1
HEADER = 4096
SLOT_DESC = 256
SLOT_DESC_BYTES = 128
FLAG_DEPTH = 1
FLAG_BOTTOM_UP = 2

CITY = "City"
BODYCAM = "Bodycam"


def _open(name: str, size: int, create: bool):
    if sys.platform == "win32":
        tag = "Local\\RaccoonSkylines." + name
        return mmap.mmap(-1, size, tagname=tag, access=mmap.ACCESS_WRITE)
    path = f"/dev/shm/RaccoonSkylines.{name}"
    if create:
        fd = os.open(path, os.O_RDWR | os.O_CREAT, 0o600)
        os.ftruncate(fd, size)
    else:
        fd = os.open(path, os.O_RDWR)
        size = os.fstat(fd).st_size
    try:
        return mmap.mmap(fd, size)
    finally:
        os.close(fd)


def stride_for(max_w: int, max_h: int, depth: bool) -> int:
    return max_w * max_h * 4 * (2 if depth else 1)


class FrameWriter:
    def __init__(self, name: str, max_w: int, max_h: int, slots: int = 3, depth: bool = False):
        self.stride = stride_for(max_w, max_h, depth)
        self.slots = slots
        self.max_w, self.max_h = max_w, max_h
        self.m = _open(name, HEADER + self.stride * slots, create=True)
        struct.pack_into("<IIIIqIIqiI", self.m, 0, MAGIC, VERSION, HEADER, slots, self.stride, max_w, max_h, 0, -1, os.getpid())
        self.publish = 0
        self.next = 0
        self.seq = [0] * slots

    def write(self, w, h, rgba: bytes, depth: bytes = None, frame=0, host_frame=0, near=0.1, far=10000.0,
              fov=60.0, bottom_up=False, daylight=1.0, infection=0.0):
        assert w <= self.max_w and h <= self.max_h and len(rgba) == w * h * 4
        if depth is not None:
            assert len(depth) == w * h * 4 and self.stride >= w * h * 8
        i = self.next
        self.next = (i + 1) % self.slots
        d = SLOT_DESC + SLOT_DESC_BYTES * i
        self.seq[i] += 1  # odd: writing
        struct.pack_into("<q", self.m, d, self.seq[i])
        base = HEADER + self.stride * i
        self.m[base:base + len(rgba)] = rgba
        if depth is not None:
            self.m[base + len(rgba):base + len(rgba) + len(depth)] = depth
        flags = (FLAG_DEPTH if depth is not None else 0) | (FLAG_BOTTOM_UP if bottom_up else 0)
        struct.pack_into("<qqIIfffIff", self.m, d + 8, frame, host_frame, w, h, near, far, fov, flags, daylight, infection)
        self.seq[i] += 1  # even: done
        struct.pack_into("<q", self.m, d, self.seq[i])
        self.publish += 1
        struct.pack_into("<qi", self.m, 32, self.publish, i)

    def close(self):
        self.m.close()


class Frame:
    __slots__ = ("w", "h", "rgba", "depth", "frame", "host_frame", "near", "far", "fov", "flags", "daylight", "infection")


class FrameReader:
    def __init__(self, name: str):
        if sys.platform == "win32":
            head = mmap.mmap(-1, HEADER, tagname="Local\\RaccoonSkylines." + name, access=mmap.ACCESS_READ)
            magic, _, _, slots, stride = struct.unpack_from("<IIIIq", head, 0)
            head.close()
            self.m = mmap.mmap(-1, HEADER + stride * slots, tagname="Local\\RaccoonSkylines." + name, access=mmap.ACCESS_READ)
        else:
            self.m = _open(name, 0, create=False)
        magic, version, _, self.slots, self.stride = struct.unpack_from("<IIIIq", self.m, 0)
        if magic != MAGIC:
            raise ValueError("not a Raccoon City Skylines frame mapping")
        self.last = -1

    def latest(self, only_new=True):
        publish, slot = struct.unpack_from("<qi", self.m, 32)
        if slot < 0 or slot >= self.slots or (only_new and publish == self.last):
            return None
        d = SLOT_DESC + SLOT_DESC_BYTES * slot
        seq = struct.unpack_from("<q", self.m, d)[0]
        if seq & 1:
            return None
        f = Frame()
        (f.frame, f.host_frame, f.w, f.h, f.near, f.far, f.fov, f.flags, f.daylight, f.infection) = \
            struct.unpack_from("<qqIIfffIff", self.m, d + 8)
        n = f.w * f.h * 4
        base = HEADER + self.stride * slot
        f.rgba = bytes(self.m[base:base + n])
        f.depth = bytes(self.m[base + n:base + 2 * n]) if f.flags & FLAG_DEPTH else None
        if struct.unpack_from("<q", self.m, d)[0] != seq:
            return None
        self.last = publish
        return f

    def close(self):
        self.m.close()
