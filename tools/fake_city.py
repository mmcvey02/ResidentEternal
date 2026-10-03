"""A stand-in for Cities: Skylines: serves the link, sends city state at 4 Hz, and publishes a synthetic skyline
(a gradient sky and a row of towers, alpha 0 below the horizon) into the City frame mapping.

Use it to develop and test the RE2 side without Cities: Skylines running:
    python tools/fake_city.py [--port 25600] [--seconds 0] [--infection 0.4] [--log out.jsonl]
"""
import argparse
import json
import math
import os
import socket
import sys
import threading
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rcsk import PORT, VERSION  # noqa: E402
from rcsk.frames import CITY, FrameWriter  # noqa: E402
from rcsk.proto import Conn  # noqa: E402


def skyline(w, h, yaw_deg, daylight):
    """RGBA bytes: sky gradient on top, towers whose silhouettes scroll with yaw, transparent below the horizon."""
    out = bytearray(w * h * 4)
    horizon = h // 2
    for x in range(w):
        a = (yaw_deg + (x / w - 0.5) * 90.0) % 360.0
        tower = int(horizon * (0.25 + 0.2 * (math.sin(math.radians(a) * 7) > 0.2) + 0.15 * math.sin(math.radians(a) * 13)))
        for y in range(h):
            i = (y * w + x) * 4
            if y >= horizon:
                continue  # alpha 0: RE2 shows through
            if y > horizon - tower:
                lit = ((x // 3) * 73856093 ^ (y // 4) * 19349663) % 7 == 0 and x % 3 and y % 4 and daylight < 0.5
                out[i:i + 4] = bytes((255, 220, 120, 255) if lit else (30, 32, 40, 255))
            else:
                t = y / horizon
                out[i:i + 4] = bytes((int(40 + 120 * daylight * t), int(60 + 140 * daylight * t), int(90 + 150 * daylight), 255))
    return bytes(out)


class FakeCity:
    def __init__(self, port=PORT, infection=0.4, log=None, frames=True, max_w=640, max_h=360):
        self.port = port
        self.infection = infection
        self.log = log
        self.received = []
        self.lock = threading.Lock()
        self.stop = threading.Event()
        self.ev_id = 0
        self.writer = FrameWriter(CITY, max_w, max_h) if frames else None
        self.srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.srv.bind(("127.0.0.1", port))
        self.srv.listen(1)
        self.srv.settimeout(0.2)

    def record(self, m):
        with self.lock:
            self.received.append(m)
        if self.log:
            self.log.write(json.dumps(m) + "\n")
            self.log.flush()

    def serve(self, seconds=0.0):
        end = time.monotonic() + seconds if seconds else None
        while not self.stop.is_set() and (end is None or time.monotonic() < end):
            try:
                s, _ = self.srv.accept()
            except socket.timeout:
                continue
            self.session(Conn(s), end)
        self.srv.close()

    def session(self, c, end):
        c.send({"t": "hello", "side": "city", "v": VERSION, "city": "Fake Raccoon City"})
        last_city = 0.0
        last_cam = None
        frame = 0
        while not self.stop.is_set() and (end is None or time.monotonic() < end):
            m = c.recv(0.05)
            if m is not None:
                self.record(m)
                if m["t"] == "cam":
                    last_cam = m
                elif m["t"] == "ev" and m.get("k") == "kill":
                    self.infection = max(0.0, self.infection - 0.01)
                elif m["t"] == "ev" and m.get("k") == "tyrant":
                    self.ev_id += 1
                    c.send({"t": "ev", "id": self.ev_id, "k": "wave", "d": {"strength": 0.5}})
            if c.closed:
                return
            now = time.monotonic()
            if now - last_city >= 0.25:
                last_city = now
                try:
                    c.send({"t": "city", "district": "Uptown", "inf": round(self.infection, 3), "cityInf": round(self.infection * 0.6, 3),
                            "power": True, "police": 0.7, "health": 0.5, "pop": 12345, "daylight": 0.3, "feed": self.writer is not None})
                except OSError:
                    return
                if self.writer and last_cam:
                    w = max(16, min(self.writer.max_w, int(last_cam.get("w", 1280)) // 4))
                    h = max(16, min(self.writer.max_h, int(last_cam.get("h", 720)) // 4))
                    q = last_cam.get("q", [0, 0, 0, 1])
                    yaw = math.degrees(2 * math.atan2(q[1], q[3]))
                    frame += 1
                    self.writer.write(w, h, skyline(w, h, yaw, 0.3), frame=frame, host_frame=int(last_cam.get("f", 0)),
                                      fov=float(last_cam.get("fov", 60)), daylight=0.3, infection=self.infection)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=PORT)
    ap.add_argument("--seconds", type=float, default=0.0)
    ap.add_argument("--infection", type=float, default=0.4)
    ap.add_argument("--no-frames", action="store_true")
    ap.add_argument("--log")
    a = ap.parse_args()
    log = open(a.log, "w") if a.log else None
    city = FakeCity(a.port, a.infection, log, frames=not a.no_frames)
    print(f"fake city listening on 127.0.0.1:{a.port}", flush=True)
    try:
        city.serve(a.seconds)
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
