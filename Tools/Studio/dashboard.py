"""Run the local FFOne Studio dashboard or update a project card from an agent."""
import argparse
import json
from pathlib import Path
import sys
import webbrowser

from dashboard.cards import Board, EDITABLE, ENUMS, ROOT


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    serve = commands.add_parser("serve")
    serve.add_argument("--port", type=int, default=8741)
    serve.add_argument("--open", action="store_true")
    for name in ("create", "update"):
        command = commands.add_parser(name)
        if name == "update":
            command.add_argument("id")
        for field in sorted(EDITABLE):
            command.add_argument("--" + field, choices=ENUMS.get(field),
                                 required=field == "title" and name == "create")
    commands.add_parser("get").add_argument("id")
    commands.add_parser("list").add_argument("--search", default="")
    args = parser.parse_args()
    board = Board()
    if args.command == "serve":
        from dashboard.server import make_server
        server = make_server(board, args.port)
        url = f"http://127.0.0.1:{server.server_port}/"
        print(url, flush=True)
        if args.open:
            webbrowser.open(url)
        with server:
            server.serve_forever()
        return
    if args.command == "get":
        result = board.get(args.id)
    elif args.command == "list":
        term = args.search.casefold()
        result = [{k: c[k] for k in ("id","kind","title","status")}
                  for c in board.state()["cards"] if term in (c["id"]+" "+c["title"]).casefold()]
    else:
        fields = {k:v for k,v in vars(args).items() if k in EDITABLE and v is not None}
        result = board.create(fields) if args.command == "create" else board.save(args.id, fields)
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
        sys.stdout.reconfigure(encoding="utf-8")
    try:
        main()
    except (ValueError, KeyError, OSError) as error:
        sys.exit(str(error))
