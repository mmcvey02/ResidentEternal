"""A stand-in for Resident Evil 2 + the plugin: connects to the city, sends an orbiting camera at 60 Hz, player state
at 4 Hz and a few scripted events, and reads back city state and skyline frames.

Use it to develop and test the Cities: Skylines mod without RE2 running:
    python tools/fake_re2.py [--port 25600] [--seconds 10] [--snapshot skyline.png]
"""
import argparse
import math
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rcsk import PORT, VERSION  # noqa: E402
from rcsk.frames import CITY, FrameReader  # noqa: E402
from rcsk.proto import Conn  # noqa: E402


def run(port=PORT, seconds=5.0, snapshot=None, events=True, connect_timeout=10.0):
    end_connect = time.monotonic() + connect_timeout
    while True:
        try:
            c = Conn.connect(port)
            break
        except OSError:
            if time.monotonic() > end_connect:
                raise
            time.sleep(0.2)
    c.send({"t": "hello", "side": "re2", "v": VERSION})
    got = {"hello": None, "city": [], "ev": [], "frames": 0, "last_frame": None}
    reader = None
    start = time.monotonic()
    f = 0
    ev_id = 0
    script = [(1.0, "kill", {"enemy": "em0000"}), (1.5, "kill", {"enemy": "em0000"}), (2.0, "tyrant", {})] if events else []
    last_player = 0.0
    while time.monotonic() - start < seconds:
        t = time.monotonic() - start
        f += 1
        yaw = t * 30.0  # degrees/s about the vertical
        h = math.radians(yaw) / 2
        c.send({"t": "cam", "f": f, "p": [math.sin(t) * 2.0, 1.6, math.cos(t) * 2.0], "q": [0.0, math.sin(h), 0.0, math.cos(h)],
                "fov": 60.0, "w": 1920, "h": 1080})
        if t - last_player >= 0.25:
            last_player = t
            c.send({"t": "player", "p": [0.0, 0.0, 0.0], "hp": 0.8, "area": "RPD_MainHall"})
        while script and t >= script[0][0]:
            _, k, d = script.pop(0)
            ev_id += 1
            c.send({"t": "ev", "id": ev_id, "k": k, "d": d})
        while True:
            m = c.recv(0.0001)
            if m is None:
                break
            if m["t"] == "hello":
                got["hello"] = m
            elif m["t"] == "city":
                got["city"].append(m)
            elif m["t"] == "ev":
                got["ev"].append(m)
        if reader is None:
            try:
                reader = FrameReader(CITY)
            except (OSError, ValueError):
                reader = None
        if reader is not None:
            fr = reader.latest()
            if fr is not None:
                got["frames"] += 1
                got["last_frame"] = fr
        time.sleep(1 / 60)
    c.close()
    if snapshot and got["last_frame"] is not None:
        save_png(snapshot, got["last_frame"])
    return got


def save_png(path, fr):
    import struct
    import zlib
    rows = []
    for y in range(fr.h):
        yy = fr.h - 1 - y if fr.flags & 2 else y
        rows.append(b"\x00" + fr.rgba[yy * fr.w * 4:(yy + 1) * fr.w * 4])
    raw = zlib.compress(b"".join(rows))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    with open(path, "wb") as fh:
        fh.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", fr.w, fr.h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", raw) + chunk(b"IEND", b""))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=PORT)
    ap.add_argument("--seconds", type=float, default=10.0)
    ap.add_argument("--snapshot")
    a = ap.parse_args()
    got = run(a.port, a.seconds, a.snapshot)
    print("hello:", got["hello"])
    print("city messages:", len(got["city"]), "last:", got["city"][-1] if got["city"] else None)
    print("city events:", got["ev"])
    print("skyline frames read:", got["frames"])


if __name__ == "__main__":
    main()
