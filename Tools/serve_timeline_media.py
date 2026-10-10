"""Loopback-only fixture for testing the existing media downloader."""
import argparse
import json
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from threading import Lock


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--truncate-path", default="", help="Send half of one file to test interrupted downloads")
    args = parser.parse_args()
    project = Path(__file__).resolve().parents[1]
    root = project / "Assets" / "StreamingAssets"
    counts = {"GET": 0, "HEAD": 0}
    count_lock = Lock()

    class Handler(SimpleHTTPRequestHandler):
        def __init__(self, *values, **options):
            super().__init__(*values, directory=str(root), **options)

        def do_GET(self):
            if self.path == "/__stats":
                with count_lock:
                    payload = json.dumps(counts).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)
                return
            with count_lock:
                counts["GET"] += 1
            if args.truncate_path and self.path == args.truncate_path:
                path = Path(self.translate_path(self.path))
                if path.is_file():
                    payload = path.read_bytes()
                    self.send_response(200)
                    self.send_header("Content-Length", str(len(payload)))
                    self.end_headers()
                    self.wfile.write(payload[: len(payload) // 2])
                    self.close_connection = True
                    return
            super().do_GET()

        def do_HEAD(self):
            with count_lock:
                counts["HEAD"] += 1
            super().do_HEAD()

        def log_message(self, *_):
            pass

    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    print(f"READY http://127.0.0.1:{server.server_port}/ root={root}", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
