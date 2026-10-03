"""End-to-end tests of the passthrough without either game: every pairing of the real implementations
(C# mod code under Mono, the RE2 plugin's portable C++, the Python reference) over the real link and shared memory."""
import json
import os
import subprocess
import sys
import threading
import time

import pytest

from conftest import ROOT, free_port

import fake_re2  # noqa: E402
from fake_city import FakeCity  # noqa: E402
from rcsk import mapping  # noqa: E402
from rcsk.frames import FrameReader, FrameWriter  # noqa: E402
from rcsk.proto import LineReader, encode  # noqa: E402

pytestmark = pytest.mark.skipif(sys.platform == "win32", reason="the tests use /dev/shm for the shared memory")


def start_fake_city(port, seconds=6.0, infection=0.4):
    city = FakeCity(port, infection)
    t = threading.Thread(target=city.serve, args=(seconds,), daemon=True)
    t.start()
    return city, t


def start_mono_city(exe, port, seconds):
    p = subprocess.Popen(["mono", exe, "city", str(port), str(seconds)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    assert p.stdout.readline().strip().startswith("link listening")
    assert p.stdout.readline().strip() == "ready"
    return p


# --- units --------------------------------------------------------------------------------------------------------

def test_line_reader_framing():
    r = LineReader()
    assert r.feed(b'{"t":"a"}\n{"t":') == [{"t": "a"}]
    assert r.feed(b'"b"}\nnot json\n{"x":1}\n') == [{"t": "b"}]
    assert r.feed(b"x" * (70 * 1024)) == []
    assert r.feed(b'tail of the long line\n{"t":"c"}\n') == [{"t": "c"}]
    assert encode({"t": "cam", "f": 1}) == b'{"t":"cam","f":1}\n'


def test_frames_python_roundtrip():
    name = f"PyTest{os.getpid()}"
    w = FrameWriter(name, 32, 16)
    try:
        r = FrameReader(name)
        assert r.latest() is None
        w.write(8, 4, bytes(range(128)), frame=7, host_frame=99, bottom_up=True, infection=0.5)
        f = r.latest()
        assert (f.frame, f.host_frame, f.w, f.h, f.flags, f.infection) == (7, 99, 8, 4, 2, 0.5)
        assert f.rgba == bytes(range(128))
        assert r.latest() is None
    finally:
        os.remove(f"/dev/shm/RaccoonSkylines.{name}")


def test_vectors_are_current():
    """protocol/vectors.json must match what the Python reference computes today."""
    with open(os.path.join(ROOT, "protocol", "vectors.json")) as fh:
        cases = json.load(fh)["cases"]
    for c in cases:
        pos, rot = mapping.map_pose(c["p"], c["q"], c["origin"], c["anchor"], c["heading"], c["scale"], c["flipZ"])
        assert pos == pytest.approx(c["pos"], abs=1e-9)
        assert rot == pytest.approx(c["rot"], abs=1e-12)


def test_mapping_properties():
    anchor, heading = (1000.0, 80.0, -500.0), 30.0
    # The origin pose lands exactly on the anchor, and rotation composes with the heading.
    pos, rot = mapping.map_pose((3, 1, 4), (0, 0, 0, 1), (3, 1, 4), anchor, heading)
    assert pos == pytest.approx(anchor)
    assert rot == pytest.approx(mapping.yaw_q(heading))
    # Moving up in RE2 moves up in the city by the same amount (times scale).
    pos, _ = mapping.map_pose((3, 3, 4), (0, 0, 0, 1), (3, 1, 4), anchor, heading, scale=2.0)
    assert pos[1] == pytest.approx(anchor[1] + 4.0)


def test_csharp_unit(csharp):
    r = subprocess.run(["mono", csharp, "unit", os.path.join(ROOT, "protocol", "vectors.json")], capture_output=True, text=True)
    assert r.returncode == 0, r.stdout + r.stderr
    assert "0 failed" in r.stdout


def test_cpp_unit(cpp):
    r = subprocess.run([cpp, "unit"], capture_output=True, text=True)
    assert r.returncode == 0, r.stdout + r.stderr


# --- pairings -----------------------------------------------------------------------------------------------------

def test_python_re2_vs_python_city():
    port = free_port()
    city, t = start_fake_city(port)
    got = fake_re2.run(port, seconds=3.0)
    city.stop.set()
    t.join(3)
    assert got["hello"]["side"] == "city"
    assert len(got["city"]) >= 8
    assert any(e["k"] == "wave" for e in got["ev"]), "Mr. X should trigger a wave"
    assert got["frames"] >= 5
    kinds = [m["t"] for m in city.received]
    assert kinds.count("cam") > 100 and "player" in kinds and "hello" in kinds
    assert sum(1 for m in city.received if m["t"] == "ev" and m["k"] == "kill") == 2


def test_cpp_re2_vs_csharp_city(cpp, csharp):
    """The plugin's real Link + FrameReader against the mod's real LinkServer + FrameWriter."""
    port = free_port()
    mono = start_mono_city(csharp, port, 7)
    try:
        r = subprocess.run([cpp, "client", str(port), "4"], capture_output=True, text=True, timeout=30)
    finally:
        out, _ = mono.communicate(timeout=30)
    summary = json.loads(r.stdout.strip().splitlines()[-1])
    assert r.returncode == 0, (r.stdout, out)
    assert summary["waves"] == 1 and summary["frames"] >= 3 and summary["lastHostFrame"] > 0
    # 5 kills (-0.004 each) and one Mr. X wave (+0.15) on a district at 0.4.
    assert summary["lastInf"] == pytest.approx(0.4 - 5 * 0.004 + 0.15, abs=1e-6)
    got = [json.loads(l[4:]) for l in out.splitlines() if l.startswith("got ")]
    assert got[0] == {"t": "hello", "side": "re2", "v": 1}
    assert sum(1 for m in got if m["t"] == "ev" and m["k"] == "kill") == 5
    assert sum(1 for m in got if m["t"] == "cam") > 100


def test_cpp_re2_vs_python_city(cpp):
    port = free_port()
    city, t = start_fake_city(port, seconds=7.0)
    r = subprocess.run([cpp, "client", str(port), "4"], capture_output=True, text=True, timeout=30)
    city.stop.set()
    t.join(3)
    assert r.returncode == 0, r.stdout


def test_python_re2_vs_csharp_city(csharp):
    port = free_port()
    mono = start_mono_city(csharp, port, 6)
    try:
        got = fake_re2.run(port, seconds=3.0, snapshot=None)
    finally:
        mono.communicate(timeout=30)
    assert got["hello"]["city"] == "Mono City"
    assert len(got["city"]) >= 8 and any(e["k"] == "wave" for e in got["ev"])
    f = got["last_frame"]
    assert f is not None and (f.w, f.h) == (320, 180) and f.flags & 2
    assert f.rgba[:4] == bytes((40, 60, 200, 255))


def test_bodycam_cpp_to_csharp(cpp, csharp):
    """The bodycam direction: the plugin's FrameWriter publishes, the mod's FrameReader shows."""
    name = f"Body{os.getpid()}"
    w = subprocess.Popen([cpp, "writer", name, "3"], stdout=subprocess.PIPE, text=True)
    time.sleep(0.3)
    r = subprocess.run(["mono", csharp, "read", name, "2"], capture_output=True, text=True, timeout=30)
    w.communicate(timeout=30)
    os.remove(f"/dev/shm/RaccoonSkylines.{name}")
    summary = json.loads(r.stdout.strip().splitlines()[-1])
    assert r.returncode == 0 and summary["frames"] >= 20 and (summary["w"], summary["h"]) == (480, 270)


def test_reconnect(cpp):
    """The plugin keeps retrying until the city appears, and survives the city restarting."""
    port = free_port()
    client = subprocess.Popen([cpp, "client", str(port), "9"], stdout=subprocess.PIPE, text=True)
    time.sleep(1.5)  # no city yet
    city, t = start_fake_city(port, seconds=2.5)
    t.join(5)
    city2, t2 = start_fake_city(port, seconds=4.0)
    out, _ = client.communicate(timeout=30)
    t2.join(5)
    assert out.count("link: connected") >= 2, out
    assert client.returncode == 0, out
