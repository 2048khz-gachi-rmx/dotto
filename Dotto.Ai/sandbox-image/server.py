import hmac
import http.server
import json
import os
import signal
import subprocess
import sys
import threading
import time
from urllib.parse import urlparse, parse_qs


PORT = int(os.environ.get("PORT", "8080"))

# Per-container random token, injected by the bot at container creation.
# Without it every request is rejected — an unauthenticated sandbox API is a
# remote code execution endpoint for anyone who can reach the port.
TOKEN = os.environ.get("SANDBOX_TOKEN", "")

# Persistent per-user storage quota. /work is a tmpfs with a hard kernel limit,
# so only /storage needs accounting.
STORAGE_QUOTA_BYTES = int(os.environ.get("SANDBOX_STORAGE_QUOTA_MB", "200")) * 1024 * 1024

WORK_PREFIX = "/work"
STORAGE_PREFIX = "/storage"
ALLOWED_ROOTS = (WORK_PREFIX, STORAGE_PREFIX)

TIMEOUTS = {
    "short": 5,
    "long": 300,
}
DEFAULT_TIMEOUT = 20
# Tool output is fed straight back to the model. Cap it so a chatty command
# cannot make the bot buffer an unbounded JSON response.
MAX_OUTPUT_CHARS = 32_000

CHUNK_SIZE = 64 * 1024


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
    """Keep the tail of stdout/stderr, capped so the JSON response stays small."""
    out, err = stdout or "", stderr or ""
    if len(out) + len(err) <= MAX_OUTPUT_CHARS:
        return out, err
    if not err:
        return _truncate(out, MAX_OUTPUT_CHARS), err
    if not out:
        return out, _truncate(err, MAX_OUTPUT_CHARS)

    half = MAX_OUTPUT_CHARS // 2
    out = _truncate(out, half)
    err = _truncate(err, MAX_OUTPUT_CHARS - len(out))
    return out, err


def _storage_usage() -> int:
    total = 0
    for root, _dirs, files in os.walk(STORAGE_PREFIX, onerror=lambda _e: None):
        for name in files:
            try:
                total += os.stat(os.path.join(root, name), follow_symlinks=False).st_size
            except OSError:
                pass
    return total


def _path_allowed(full: str) -> bool:
    return any(full == root or full.startswith(root + os.sep) for root in ALLOWED_ROOTS)


def _resolve_path(raw: str, for_write: bool):
    """Resolve a client-supplied path, refusing anything outside /work and /storage.

    Paths are resolved with realpath, so symlinks cannot be used to escape the
    allowed roots. For writes the parent is resolved (the file itself may not
    exist yet).
    """
    if not raw or not raw.startswith("/") or "\x00" in raw:
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

    # -- plumbing ---------------------------------------------------------

    def log_message(self, format, *args):
        sys.stderr.write(f"[sandbox] {self.address_string()} {args[0]}\n")

    def _send_json(self, status: int, body: dict):
        data = json.dumps(body).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _authorized(self) -> bool:
        if not TOKEN:
            self._send_json(503, {"error": "sandbox token is not configured"})
            return False
        supplied = self.headers.get("X-Sandbox-Token", "")
        if not hmac.compare_digest(supplied.encode(), TOKEN.encode()):
            self._send_json(401, {"error": "unauthorized"})
            return False
        return True

    def _read_json(self) -> dict:
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0:
            return {}
        raw = self.rfile.read(length)
        return json.loads(raw)

    # -- routes -----------------------------------------------------------

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

    # -- file transfer ----------------------------------------------------

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
                    chunk = source.read(CHUNK_SIZE)
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

        # Enforce the persistent-storage quota before accepting the bytes.
        if full.startswith(STORAGE_PREFIX + os.sep) and _storage_usage() + length > STORAGE_QUOTA_BYTES:
            self._send_json(507, {"error": "storage quota exceeded"})
            return

        os.makedirs(os.path.dirname(full), exist_ok=True)

        target = _unique_path(full) if dedupe else full
        temporary = f"{target}.part"

        try:
            remaining = length
            with open(temporary, "wb") as sink:
                while remaining > 0:
                    chunk = self.rfile.read(min(CHUNK_SIZE, remaining))
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
            try:
                os.unlink(temporary)
            except OSError:
                pass
            self._send_json(500, {"error": str(error)})
            return

        self._send_json(200, {"path": target})

    # -- execution --------------------------------------------------------

    def _execute(self, command: str, cwd: str, timeout: int, initial_usage: int):
        try:
            process = subprocess.Popen(
                command,
                shell=True,
                cwd=cwd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                start_new_session=True,
            )
        except FileNotFoundError:
            return 200, {"exitCode": -1, "stdout": "", "stderr": f"Command not found: {command}"}
        except OSError as error:
            return 200, {"exitCode": -1, "stdout": "", "stderr": str(error)}

        breached = [False]

        def watchdog():
            # Kill the whole process group if this command pushes /storage over
            # quota. Commands that *reduce* usage (cleanup) are left alone.
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

        watcher = threading.Thread(target=watchdog, daemon=True)
        watcher.start()

        try:
            stdout, stderr = process.communicate(timeout=timeout)
        except subprocess.TimeoutExpired:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except OSError:
                pass
            stdout, stderr = process.communicate()
            stdout, stderr = _cap_output(
                stdout, (stderr or "") + f"\nCommand timed out after {timeout}s"
            )
            return 200, {
                "exitCode": -1,
                "stdout": stdout,
                "stderr": stderr,
            }

        if breached[0]:
            stderr = (stderr or "") + "\nStorage quota exceeded; process killed."

        stdout, stderr = _cap_output(stdout, stderr)
        return 200, {
            "exitCode": process.returncode,
            "stdout": stdout,
            "stderr": stderr,
        }


class SandboxServer(http.server.ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True


def main():
    server = SandboxServer(("0.0.0.0", PORT), SandboxHandler)
    sys.stderr.write(f"[sandbox] listening on 0.0.0.0:{PORT}\n")
    sys.stderr.flush()
    server.serve_forever()


if __name__ == "__main__":
    main()
