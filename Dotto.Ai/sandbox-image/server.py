import http.server
import json
import os
import subprocess
import sys


PORT = int(os.environ.get("PORT", "8080"))

TIMEOUTS = {
    "short": 5,
    "long": 300,
}
DEFAULT_TIMEOUT = 20

WORK_PREFIX = "/work"


class SandboxHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        sys.stderr.write(f"[sandbox] {args[0]} {args[1]} {args[2]}\n")

    def _send_json(self, status: int, body: dict):
        data = json.dumps(body).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _read_body(self) -> dict:
        length = int(self.headers.get("Content-Length", "0"))
        if length == 0:
            return {}
        raw = self.rfile.read(length)
        return json.loads(raw)

    def _resolve_cwd(self, cwd: str) -> str:
        if not cwd:
            return WORK_PREFIX
        if os.path.isabs(cwd):
            return cwd
        return os.path.join(WORK_PREFIX, cwd)

    def do_GET(self):
        if self.path == "/ping":
            self._send_json(200, {"status": "ok"})
        else:
            self._send_json(404, {"error": "not found"})

    def do_POST(self):
        if self.path != "/execute":
            self._send_json(404, {"error": "not found"})
            return

        try:
            body = self._read_body()
        except (json.JSONDecodeError, UnicodeDecodeError) as e:
            self._send_json(400, {"error": "invalid json"})
            return

        command = body.get("command", "")
        cwd = body.get("cwd", "")
        timeout_key = body.get("timeout", "")

        if not command:
            self._send_json(400, {"error": "command is required"})
            return

        resolved_cwd = self._resolve_cwd(cwd)

        timeout = TIMEOUTS.get(timeout_key, DEFAULT_TIMEOUT)

        try:
            result = subprocess.run(
                command,
                shell=True,
                cwd=resolved_cwd,
                capture_output=True,
                text=True,
                timeout=timeout,
            )
            self._send_json(200, {
                "exitCode": result.returncode,
                "stdout": result.stdout,
                "stderr": result.stderr,
            })
        except subprocess.TimeoutExpired:
            self._send_json(200, {
                "exitCode": -1,
                "stdout": "",
                "stderr": f"Command timed out after {timeout}s",
            })
        except FileNotFoundError:
            self._send_json(200, {
                "exitCode": -1,
                "stdout": "",
                "stderr": f"Command not found: {command}",
            })
        except OSError as e:
            self._send_json(200, {
                "exitCode": -1,
                "stdout": "",
                "stderr": str(e),
            })


def main():
    server = http.server.HTTPServer(("0.0.0.0", PORT), SandboxHandler)
    sys.stderr.write(f"[sandbox] listening on 0.0.0.0:{PORT}\n")
    sys.stderr.flush()
    server.serve_forever()


if __name__ == "__main__":
    main()
