"""Loopback HTTP API for the dashboard and source-backed architecture window."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import html
import json
import logging
import mimetypes
from pathlib import Path
import re
from urllib.parse import parse_qs, urlsplit

from .cards import lock

STATIC = Path(__file__).parent / "static"


def source_page(root, query):
    raw = query.get("file", [""])[0]
    path = (root / raw).resolve()
    trees = [root / "Assets/FFOne", root / "Docs/Studio", root / "Tools/Studio",
             root.parent / "RustyFusion/src", root.parent / "RustyFusion/sql",
             root.parent / "FFOneClient/crates/ffone-runtime-contracts"]
    configs = [root.parent / "RustyFusion/Cargo.toml", root / "Packages/manifest.json"]
    if (path not in configs and not any(path.is_relative_to(t) for t in trees)) or not path.is_file():
        raise KeyError(raw)
    if path.suffix.lower() not in (".cs", ".rs", ".sql", ".toml", ".asmdef", ".md", ".json", ".py", ".unity"):
        raise KeyError(raw)
    if path.stat().st_size > 2_000_000:
        raise ValueError("Файл слишком велик для просмотра")
    line = max(1,int(query.get("line", ["1"])[0]))
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    start = min(line-1, max(0,len(lines)-1))
    excerpt = "\n".join(f"{i+1:5}  {v}" for i,v in enumerate(lines) if start-12 <= i <= start+90)
    return ('<!doctype html><meta charset=utf-8><title>' + html.escape(path.name)
            + '</title><h3>' + html.escape(raw) + '</h3><pre>' + html.escape(excerpt) + '</pre>')


def make_server(board, port=8741):
    class Handler(BaseHTTPRequestHandler):
        def send(self, data, mime="application/json; charset=utf-8", code=200):
            if isinstance(data, (list, dict)):
                data = json.dumps(data, ensure_ascii=False)
            if isinstance(data, str):
                data = data.encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", mime)
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Referrer-Policy", "no-referrer")
            self.end_headers()
            self.wfile.write(data)

        def handle_request(self, write=False):
            host = self.headers.get("Host", "")
            allowed = {f"127.0.0.1:{self.server.server_port}", f"localhost:{self.server.server_port}"}
            if host not in allowed or (write and (self.headers.get("X-Studio") != "1" or
                   self.headers.get("Origin", "http://" + host) != "http://" + host)):
                return self.send({"error":"Недопустимый источник запроса"}, code=403)
            route = urlsplit(self.path)
            parts = route.path.strip("/").split("/")
            try:
                if write:
                    length = int(self.headers.get("Content-Length", 0))
                    if not 0 < length <= 180000:
                        raise ValueError("Недопустимый размер запроса")
                    payload = json.loads(self.rfile.read(length))
                    if not isinstance(payload, dict):
                        raise ValueError("Ожидался объект JSON")
                    if parts == ["api", "cards"]:
                        return self.send(board.create(payload), code=201)
                    if len(parts) == 3 and parts[:2] == ["api", "cards"]:
                        return self.send(board.save(parts[2], payload))
                    if len(parts) == 3 and parts[:2] == ["api", "chat"]:
                        from .chatbridge import launch
                        key = parts[2]
                        board.path(key)
                        triage = payload.get("triage", False)
                        if type(triage) is not bool:
                            raise ValueError("Неизвестный режим чата")
                        field = "triageChat" if triage else "chat"
                        with lock(board.root, "launch-" + key, timeout=60):
                            card = board.get(key)
                            card["chat"] = card[field]
                            result = launch(board.root, card, triage)
                            board.save(key, {field:result["chat"]}, internal=True)
                        return self.send(result)
                else:
                    if route.path == "/api/state":
                        return self.send(board.state())
                    if route.path == "/api/health":
                        return self.send({"app":"ffone-studio","root":str(board.root)})
                    if route.path == "/api/architecture":
                        from .architecture import graph
                        return self.send(graph(board.root))
                    if route.path == "/source":
                        return self.send(source_page(board.root, parse_qs(route.query)), "text/html; charset=utf-8")
                    page = {"/":"index.html","/architecture":"architecture.html"}.get(route.path)
                    if not page and len(parts)==2 and parts[0]=="static" and re.fullmatch(r"[a-zA-Z0-9_.-]+",parts[1]):
                        page = parts[1]
                    if page and (STATIC/page).is_file():
                        return self.send((STATIC/page).read_bytes(), mimetypes.guess_type(page)[0] or "application/octet-stream")
                self.send({"error":"Не найдено"}, code=404)
            except KeyError as exc:
                self.send({"error":str(exc)}, code=404)
            except (ValueError, TimeoutError) as exc:
                self.send({"error":str(exc)}, code=400)
            except (ConnectionError, BrokenPipeError):
                pass
            except Exception as exc:
                logging.exception("Studio request failed")
                self.send({"error":str(exc)}, code=500)

        def do_GET(self):
            self.handle_request()

        def do_POST(self):
            self.handle_request(write=True)

    return ThreadingHTTPServer(("127.0.0.1",port), Handler)
