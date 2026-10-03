"""The link: newline-delimited JSON over TCP on 127.0.0.1."""
import json
import socket

MAX_LINE = 64 * 1024


def encode(msg: dict) -> bytes:
    return (json.dumps(msg, separators=(",", ":")) + "\n").encode("utf-8")


class LineReader:
    """Splits a byte stream into JSON messages; drops over-long or malformed lines."""

    def __init__(self):
        self.buf = b""
        self.dropping = False

    def feed(self, data: bytes):
        self.buf += data
        out = []
        while True:
            i = self.buf.find(b"\n")
            if i < 0:
                if len(self.buf) > MAX_LINE:
                    self.buf = b""
                    self.dropping = True
                return out
            line, self.buf = self.buf[:i], self.buf[i + 1:]
            if self.dropping:
                self.dropping = False
                continue
            if not line.strip() or len(line) > MAX_LINE:
                continue
            try:
                msg = json.loads(line.decode("utf-8"))
            except (ValueError, UnicodeDecodeError):
                continue
            if isinstance(msg, dict) and isinstance(msg.get("t"), str):
                out.append(msg)


class Conn:
    """A blocking link connection with a receive timeout."""

    def __init__(self, sock: socket.socket):
        self.sock = sock
        self.reader = LineReader()
        self.pending = []
        self.closed = False

    @classmethod
    def connect(cls, port: int, host: str = "127.0.0.1", timeout: float = 5.0):
        s = socket.create_connection((host, port), timeout=timeout)
        s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        return cls(s)

    def send(self, msg: dict):
        self.sock.sendall(encode(msg))

    def recv(self, timeout: float = 5.0):
        """Next message, or None on timeout/close."""
        if self.pending:
            return self.pending.pop(0)
        self.sock.settimeout(timeout)
        try:
            while not self.pending:
                data = self.sock.recv(65536)
                if not data:
                    self.closed = True
                    return None
                self.pending.extend(self.reader.feed(data))
        except socket.timeout:
            return None
        except OSError:
            self.closed = True
            return None
        return self.pending.pop(0)

    def wait_for(self, t: str, timeout: float = 5.0, pred=None):
        import time
        end = time.monotonic() + timeout
        while time.monotonic() < end:
            m = self.recv(max(0.01, end - time.monotonic()))
            if m is None:
                continue
            if m.get("t") == t and (pred is None or pred(m)):
                return m
        return None

    def close(self):
        try:
            self.sock.close()
        except OSError:
            pass
