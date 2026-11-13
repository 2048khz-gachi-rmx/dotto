import hmac
import http.server
import json
import os
import select
import signal
import socketserver
import subprocess
import sys
import threading
import time
from urllib.parse import urlparse, parse_qs


PORT = int(os.environ.get("PORT", "8080"))

# Production: bind this Unix socket instead of TCP.
SANDBOX_SOCKET = os.environ.get("SANDBOX_SOCKET", "")

# Per-container API token; unset means every request is refused.
TOKEN = os.environ.get("SANDBOX_TOKEN", "")

STORAGE_QUOTA_BYTES = int(os.environ.get("SANDBOX_STORAGE_QUOTA_MB", "200")) * 1024 * 1024

WORK_PREFIX = "/work"
STORAGE_PREFIX = "/storage"
ALLOWED_ROOTS = (WORK_PREFIX, STORAGE_PREFIX)

TIMEOUTS = {
    "short": 5,
    "long": 300,
}
DEFAULT_TIMEOUT = 20
# The only output cap; see sandbox-execution "Bounded Response Size".
MAX_OUTPUT_CHARS = 10_000
# 4 bytes/char: guarantees MAX_OUTPUT_CHARS survive decoding, flat memory.
MAX_OUTPUT_BYTES = 4 * MAX_OUTPUT_CHARS

# How long to wait for the pipe readers once the child has exited. Bounds the
# request when a background grandchild keeps the inherited pipes open.
DRAIN_TIMEOUT = 5

CHUNK_SIZE = 64 * 1024
# 1 MiB is the transfer knee under gVisor; exec pumps keep CHUNK_SIZE.
FILE_CHUNK_SIZE = 1024 * 1024

# Minimum per-file quota charge, so tiny files are not free.
MIN_FILE_CHARGE_BYTES = 256 * 1024

# Longest accepted path (also bounds the echoed path in responses).
MAX_PATH_CHARS = 4096


def _truncate(text: str, limit: int) -> str:
    if len(text) <= limit:
        return text
    if limit <= 0:
        return ""
    marker = f"[... {len(text) - limit} chars truncated]\n"
    if len(marker) >= limit:
        return text[-limit:]
    return marker + text[-(limit - len(marker)):]


def _cap_output(stdout, stderr):
    """Keep the tail of both streams inside one shared MAX_OUTPUT_CHARS budget."""
    out, err = stdout or "", stderr or ""
    if len(out) + len(err) <= MAX_OUTPUT_CHARS:
        return out, err

    stderr_cap = min(len(err), max(MAX_OUTPUT_CHARS // 2, MAX_OUTPUT_CHARS - len(out)))
    stdout_cap = min(len(out), MAX_OUTPUT_CHARS - stderr_cap)
    return _truncate(out, stdout_cap), _truncate(err, stderr_cap)


class _TailBuffer:
    """Retains the last `limit` bytes plus a dropped-byte count, so memory stays flat."""

    __slots__ = ("limit", "_data", "dropped")

    def __init__(self, limit: int):
        self.limit = limit
        self._data = bytearray()
        self.dropped = 0

    def write(self, chunk: bytes) -> None:
        if not chunk:
            return
        self._data += chunk
        overflow = len(self._data) - self.limit
        if overflow > 0:
            del self._data[:overflow]
            self.dropped += overflow

    def text(self, limit: int) -> str:
        # Decode once, at the end, replacing invalid sequences: arbitrary child
        # output must never fail the request.
        text = bytes(self._data).decode("utf-8", errors="replace")
        if not self.dropped:
            return _truncate(text, limit)
        # The marker reports dropped *bytes* (that is what this buffer
        # counts) and its own length counts against `limit`, so the result
        # never exceeds the cap.
        marker = f"[... {self.dropped} bytes truncated]\n"
        room = max(0, limit - len(marker))
        return (marker + (text[-room:] if room else ""))[:limit]


def _pump_and_close(stream, tail: _TailBuffer, stop: threading.Event) -> None:
    """Drain one pipe into `tail`, noticing `stop` even while a grandchild holds the pipe."""
    try:
        fd = stream.fileno()
        while not stop.is_set():
            try:
                ready, _, _ = select.select([fd], [], [], 0.2)
            except (OSError, ValueError):
                return
            if not ready:
                continue
            try:
                chunk = os.read(fd, CHUNK_SIZE)
            except OSError:
                # Pipe torn down by a watchdog/timeout kill.
                return
            if not chunk:
                return
            tail.write(chunk)
    finally:
        try:
            stream.close()
        except OSError:
            pass


def _json_body(payload: dict) -> bytes:
    return json.dumps(payload).encode("utf-8")


def _storage_usage() -> int:
    total = 0
    for root, _dirs, files in os.walk(STORAGE_PREFIX, onerror=lambda _e: None):
        for name in files:
            try:
                size = os.stat(os.path.join(root, name), follow_symlinks=False).st_size
            except OSError:
                continue
            total += max(size, MIN_FILE_CHARGE_BYTES)
    return total


def _path_allowed(full: str) -> bool:
    return any(full == root or full.startswith(root + os.sep) for root in ALLOWED_ROOTS)


def _resolve_path(raw: str, for_write: bool):
    """Resolve a path inside /work or /storage; writes resolve the parent (for_write)."""
    # Bound the echo size too: the response repeats the path, and an
    # unbounded caller-supplied string must not be able to grow a payload
    # past the response-size invariant.
    if not raw or not raw.startswith("/") or "\x00" in raw or len(raw) > MAX_PATH_CHARS:
        return None

    parts = [part for part in raw.split("/") if part not in ("", ".")]
    if not parts or any(part == ".." for part in parts):
        return None

    if for_write:
        parent = os.path.realpath(os.path.dirname(raw))
        full = os.path.join(parent, os.path.basename(raw))
    else:
        full = os.path.realpath(raw)

    return full if _path_allowed(full) else None


def _unique_path(full: str) -> str:
    if not os.path.exists(full):
        return full
    stem, ext = os.path.splitext(full)
    index = 1
    while os.path.exists(f"{stem}_{index}{ext}"):
        index += 1
    return f"{stem}_{index}{ext}"


class SandboxHandler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def address_string(self):
        # AF_UNIX has an empty client_address; the base class would index it.
        if not self.client_address:
            return "unix"
        return super().address_string()

    def log_message(self, format, *args):
        sys.stderr.write(f"[sandbox] {self.address_string()} {args[0]}\n")

    def _send_json(self, status: int, body: dict, close: bool = False):
        data = _json_body(body)
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        if close:
            self.send_header("Connection", "close")
            self.close_connection = True
        self.end_headers()
        self.wfile.write(data)

    def _authorized(self) -> bool:
        # Reject without reading the body: on a keep-alive socket it would be parsed as the next request.
        if not TOKEN:
            self._send_json(503, {"error": "sandbox token is not configured"}, close=True)
            return False
        supplied = self.headers.get("X-Sandbox-Token", "")
        if not hmac.compare_digest(supplied.encode(), TOKEN.encode()):
            self._send_json(401, {"error": "unauthorized"}, close=True)
            return False
        return True

    def _read_json(self) -> dict:
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0:
            return {}
        raw = self.rfile.read(length)
        return json.loads(raw)

    def do_GET(self):
        if not self._authorized():
            return

        parsed = urlparse(self.path)

        if parsed.path == "/ping":
            self._send_json(200, {"status": "ok"})
            return

        if parsed.path == "/file":
            self._get_file(parse_qs(parsed.query))
            return

        self._send_json(404, {"error": "not found"})

    def do_PUT(self):
        if not self._authorized():
            return

        parsed = urlparse(self.path)
        if parsed.path != "/file":
            self._send_json(404, {"error": "not found"})
            return

        self._put_file(parse_qs(parsed.query))

    def do_POST(self):
        if not self._authorized():
            return

        parsed = urlparse(self.path)
        if parsed.path != "/execute":
            self._send_json(404, {"error": "not found"})
            return

        try:
            body = self._read_json()
        except (json.JSONDecodeError, UnicodeDecodeError):
            self._send_json(400, {"error": "invalid json"})
            return

        command = body.get("command", "")
        cwd = body.get("cwd", "")
        timeout_key = body.get("timeout", "")

        if not command:
            self._send_json(400, {"error": "command is required"})
            return

        resolved_cwd = cwd if cwd and os.path.isabs(cwd) else os.path.join(WORK_PREFIX, cwd)
        timeout = TIMEOUTS.get(timeout_key, DEFAULT_TIMEOUT)

        initial_usage = _storage_usage()
        if initial_usage > STORAGE_QUOTA_BYTES:
            sys.stderr.write(
                f"[sandbox] storage over quota before execute: {initial_usage} > {STORAGE_QUOTA_BYTES}\n"
            )

        self._send_json(*self._execute(command, resolved_cwd, timeout, initial_usage))

    def _get_file(self, query: dict):
        raw_path = (query.get("path") or [""])[0]
        full = _resolve_path(raw_path, for_write=False)

        if full is None:
            self._send_json(400, {"error": "invalid path"})
            return

        if not os.path.isfile(full):
            self._send_json(404, {"error": "not found"})
            return

        size = os.path.getsize(full)
        self.send_response(200)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(size))
        self.end_headers()

        try:
            with open(full, "rb") as source:
                while True:
                    chunk = source.read(FILE_CHUNK_SIZE)
                    if not chunk:
                        break
                    self.wfile.write(chunk)
        except (BrokenPipeError, ConnectionResetError):
            self.log_message("client disconnected during download", "", "")
        except OSError as error:
            # Headers are already out; the truncated body will be detected by
            # the bot's Content-Length accounting.
            sys.stderr.write(f"[sandbox] read error serving {full}: {error}\n")

    def _put_file(self, query: dict):
        raw_path = (query.get("path") or [""])[0]
        dedupe = (query.get("dedupe") or [""])[0].lower() in ("1", "true", "yes")
        full = _resolve_path(raw_path, for_write=True)

        if full is None:
            self._send_json(400, {"error": "invalid path"})
            return

        length = int(self.headers.get("Content-Length", "0") or "0")
        if length <= 0:
            self._send_json(411, {"error": "Content-Length is required"})
            return

        if full.startswith(STORAGE_PREFIX + os.sep) and (
            _storage_usage() + max(length, MIN_FILE_CHARGE_BYTES) > STORAGE_QUOTA_BYTES
        ):
            self._send_json(507, {"error": "storage quota exceeded"})
            return

        temporary = None
        try:
            # Inside the try: a failing makedirs used to escape the handler and
            # drop the connection without a response, which the bot sees as a
            # transport error rather than a tool result.
            os.makedirs(os.path.dirname(full), exist_ok=True)

            target = _unique_path(full) if dedupe else full
            temporary = f"{target}.part"

            remaining = length
            with open(temporary, "wb") as sink:
                while remaining > 0:
                    chunk = self.rfile.read(min(FILE_CHUNK_SIZE, remaining))
                    if not chunk:
                        raise OSError("client closed the connection before sending the declared body")
                    sink.write(chunk)
                    remaining -= len(chunk)

            # `temporary` is already counted by _storage_usage(), so compare
            # directly instead of adding length a second time.
            if target.startswith(STORAGE_PREFIX + os.sep) and _storage_usage() > STORAGE_QUOTA_BYTES:
                os.unlink(temporary)
                self._send_json(507, {"error": "storage quota exceeded"})
                return

            os.replace(temporary, target)
        except OSError as error:
            if temporary is not None:
                try:
                    os.unlink(temporary)
                except OSError:
                    pass
            self._send_json(500, {"error": str(error)})
            return

        self._send_json(200, {"path": target})

    def _execute(self, command: str, cwd: str, timeout: int, initial_usage: int):
        if not os.path.isdir(cwd):
            return 200, {
                "exitCode": -1,
                "stdout": "",
                "stderr": f"Working directory does not exist: {cwd}",
            }

        try:
            process = subprocess.Popen(
                command,
                shell=True,
                cwd=cwd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                # Binary pipes: children emit arbitrary bytes (`cat /bin/ls`).
                # Decoding happens once after the drain, with errors="replace".
                start_new_session=True,
            )
        except FileNotFoundError as error:
            # The shell itself is missing (the command is not on PATH): report the
            # message the OS gave us, without a traceback, so the model can act.
            return 200, {"exitCode": -1, "stdout": "", "stderr": str(error)}
        except OSError as error:
            return 200, {"exitCode": -1, "stdout": "", "stderr": str(error)}

        breached = [False]

        def watchdog():
            # Kill the process group on a quota breach; cleanup-only commands are left alone.
            while process.poll() is None:
                time.sleep(1)
                if process.poll() is not None:
                    return
                usage = _storage_usage()
                if usage > STORAGE_QUOTA_BYTES and usage > initial_usage:
                    breached[0] = True
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except OSError:
                        pass
                    return

        # Drain both pipes from the start so a full pipe cannot block the child.
        stdout_tail = _TailBuffer(MAX_OUTPUT_BYTES)
        stderr_tail = _TailBuffer(MAX_OUTPUT_BYTES)
        stop = threading.Event()
        readers = [
            threading.Thread(
                target=_pump_and_close,
                args=(process.stdout, stdout_tail, stop),
                daemon=True,
            ),
            threading.Thread(
                target=_pump_and_close,
                args=(process.stderr, stderr_tail, stop),
                daemon=True,
            ),
        ]
        for reader in readers:
            reader.start()

        watcher = threading.Thread(target=watchdog, daemon=True)
        watcher.start()

        timed_out = False
        try:
            returncode = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except OSError:
                pass
            try:
                returncode = process.wait(timeout=DRAIN_TIMEOUT)
            except subprocess.TimeoutExpired:
                returncode = -1

        # One shared deadline for both readers, then stop them so threads/fds do not outlive the request.
        deadline = time.monotonic() + DRAIN_TIMEOUT
        for reader in readers:
            reader.join(timeout=max(0.0, deadline - time.monotonic()))
        stop.set()
        for reader in readers:
            reader.join(timeout=0.5)

        stdout = stdout_tail.text(MAX_OUTPUT_CHARS)
        stderr = stderr_tail.text(MAX_OUTPUT_CHARS)

        if timed_out:
            stderr = stderr + f"\nCommand timed out after {timeout}s"
            returncode = -1
        elif breached[0]:
            # A deliberate server-side kill, not the command's own exit status.
            stderr = stderr + "\nStorage quota exceeded; process killed."
            returncode = -1

        stdout, stderr = _cap_output(stdout, stderr)
        return 200, {
            "exitCode": returncode,
            "stdout": stdout,
            "stderr": stderr,
        }


class SandboxServer(http.server.ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True


class SandboxUnixServer(socketserver.ThreadingUnixStreamServer):
    daemon_threads = True


def main():
    if SANDBOX_SOCKET:
        # A leftover socket from a previous container run would make bind() fail.
        try:
            os.unlink(SANDBOX_SOCKET)
        except FileNotFoundError:
            pass

        os.makedirs(os.path.dirname(SANDBOX_SOCKET) or ".", exist_ok=True)
        server = SandboxUnixServer(SANDBOX_SOCKET, SandboxHandler)
        # The host bot runs as root; do not expose the control socket to other host users.
        os.chmod(SANDBOX_SOCKET, 0o600)
        sys.stderr.write(f"[sandbox] listening on unix:{SANDBOX_SOCKET}\n")
        sys.stderr.flush()
        server.serve_forever()
        return

    server = SandboxServer(("0.0.0.0", PORT), SandboxHandler)
    sys.stderr.write(f"[sandbox] listening on 0.0.0.0:{PORT}\n")
    sys.stderr.flush()
    server.serve_forever()


if __name__ == "__main__":
    main()
