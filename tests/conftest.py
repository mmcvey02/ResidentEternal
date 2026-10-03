import os
import shutil
import socket
import subprocess
import sys

import pytest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))


def free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


@pytest.fixture(scope="session")
def csharp():
    """Builds the C# mod (against stubs) and Tests.exe; returns the path to Tests.exe."""
    if not shutil.which("mcs") or not shutil.which("mono"):
        pytest.skip("Mono (mcs, mono) not installed")
    subprocess.run([os.path.join(ROOT, "cities", "build.sh"), "--no-test"], check=True)
    return os.path.join(ROOT, "cities", "build", "Tests.exe")


@pytest.fixture(scope="session")
def cpp():
    """Builds re2/build/test_common from the plugin's portable sources."""
    cxx = shutil.which("g++") or shutil.which("clang++")
    if not cxx:
        pytest.skip("no C++ compiler")
    out = os.path.join(ROOT, "re2", "build", "test_common")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    src = os.path.join(ROOT, "re2")
    subprocess.run([cxx, "-std=c++20", "-O1", "-Wall", "-Wextra", "-Werror", "-pthread", "-o", out,
                    os.path.join(src, "tests", "test_common.cpp"),
                    os.path.join(src, "src", "common", "json_lite.cpp"),
                    os.path.join(src, "src", "common", "link.cpp"),
                    os.path.join(src, "src", "common", "frames.cpp")], check=True)
    return out
