"""Read-only source index for the Studio architecture window."""
import os
from pathlib import Path
import re


class SourceIndex:
    def __init__(self, root):
        self.root = Path(root)
        self.nodes = {}
        self.edges = {}
        self.missing = []

    def source(self, path, line):
        return {"file": Path(os.path.relpath(path, self.root)).as_posix(), "line": line}

    def read(self, path):
        try:
            return path.read_text(encoding="utf-8-sig")
        except OSError:
            self.missing.append(self.source(path, 1)["file"])
            return ""

    def node(self, key, title, group, path, line):
        return self.nodes.setdefault(key, {"id": key, "name": title, "group": group,
                                           "source": self.source(path, line), "items": []})

    def edge(self, left, right, label, path, line):
        if left == right or left not in self.nodes or right not in self.nodes:
            return
        key = left, right, label
        edge = self.edges.setdefault(key, {"from": left, "to": right, "label": label,
                                           "sources": [], "count": 0})
        edge["count"] += 1
        if len(edge["sources"]) < 4:
            edge["sources"].append(self.source(path, line))


def line_at(text, offset):
    return text.count("\n", 0, offset) + 1


def index_client(index):
    runtime = index.root / "Assets/FFOne/Runtime"
    if not runtime.is_dir():
        index.missing.append(index.source(runtime, 1)["file"])
        return
    imports = []
    for path in sorted(runtime.rglob("*.cs")):
        code = index.read(path)
        namespace = re.search(r"^\s*namespace\s+(FFOne[.\w]*)", code, re.M)
        if not namespace:
            continue
        name = namespace[1]
        node = index.node("cs:" + name, name, "client", path, line_at(code, namespace.start(1)))
        node["items"].append({"name": path.name, **index.source(path, 1)})
        for match in re.finditer(r"^\s*using\s+(FFOne[.\w]*);", code, re.M):
            imports.append((node["id"], "cs:" + match[1], "using", path, line_at(code, match.start(1))))
    for args in imports:
        index.edge(*args)


def index_server(index, server):
    src = server / "src"
    root_mod = src / "lib.rs"
    code = index.read(root_mod)
    names = set(re.findall(r"^pub mod (\w+);", code, re.M))
    sources = []
    for path in sorted(src.rglob("*.rs")):
        parts = path.relative_to(src).parts
        name = Path(parts[0]).stem
        if name not in names or path.stem.endswith("_tests") or path.stem == "test_suite":
            continue
        if name == "server" and len(parts) > 2:
            name += "::" + parts[1]
        node = index.node("rs:" + name, name, "server", path, 1)
        if path.stem == "mod":
            node["source"] = index.source(path, 1)
        node["items"].append({"name": "/".join(parts), **index.source(path, 1)})
        sources.append((path, node["id"]))
    for path, owner in sources:
        code = index.read(path)
        for match in re.finditer(r"^\s*(?:pub\s+)?use\s+crate::([^;]+);", code, re.M):
            raw = match[1].lstrip()
            origin = match.end(1) - len(raw)
            if raw.startswith("{"):
                # Only the first-level names of a use tree refer to crate modules.
                depth = 0
                start = 1
                tokens = []
                for pos, char in enumerate(raw[1:], 1):
                    if char in ",}" and depth == 0:
                        tokens.append((raw[start:pos], start))
                        start = pos + 1
                    if char == "{":
                        depth += 1
                    elif char == "}":
                        depth -= 1
            else:
                tokens = [(raw, 0)]
            for token, offset in tokens:
                mod = re.match(r"\s*(\w+)", token)
                if mod and mod[1] in names:
                    index.edge(owner, "rs:" + mod[1], "use crate", path,
                               line_at(code, origin + offset + mod.start(1)))
        if owner == "rs:server":
            for match in re.finditer(r"^pub mod (login|shard);", code, re.M):
                index.edge(owner, "rs:server::" + match[1], "mod", path, line_at(code, match.start()))


def index_bootstrap(index):
    scene = index.root / "Assets/FFOne/Scenes/Bootstrap/Main.unity"
    text = index.read(scene)
    records = list(re.finditer(r"^--- !u!(\d+) &(-?\d+)\s*$", text, re.M))
    links = []
    for i, record in enumerate(records):
        if record[1] != "114":
            continue
        body = text[record.end():records[i + 1].start() if i + 1 < len(records) else len(text)]
        cls = re.search(r"m_EditorClassIdentifier:\s*[^\n:]+::([\w.]+)", body)
        if not cls:
            continue
        key = "scene:" + record[2]
        pos = record.end()
        node = index.node(key, cls[1].split(".")[-1], "scene", scene, line_at(text, pos + cls.start()))
        node["items"].append({"name": cls[1], **node["source"]})
        for field in re.finditer(r"^  (\w+): \{fileID: (-?\d+)\}$", body, re.M):
            if field[2] != "0" and not field[1].startswith("m_"):
                links.append((key, "scene:" + field[2], field[1], scene, line_at(text, pos + field.start())))
    for args in links:
        index.edge(*args)


TABLE = re.compile(r"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(\w+)\s*\((.*?)\)\s*;", re.I | re.S)
FOREIGN = re.compile(r"REFERENCES\s+(\w+)\s*\(([^)]+)\)", re.I)


def index_database(index, server, engine):
    sql = server / "sql"
    schema = sql / ("sqlite/create_tables.sql" if engine == "sqlite" else "create_tables.sql")
    delayed = []
    by_name = {}
    for path in [schema, *(p for p in sorted(sql.glob("create_*.sql")) if p != sql / "create_tables.sql")]:
        text = index.read(path)
        for table in TABLE.finditer(text):
            name = table[1]
            key = engine + ":" + name
            if key in index.nodes:  # Shared idempotent migration after base schema.
                continue
            node = index.node(key, name, engine, path, line_at(text, table.start()))
            by_name[name.lower()] = key
            node["queries"] = []
            for n, raw in enumerate(table[2].splitlines(), line_at(text, table.start(2))):
                field = raw.strip().rstrip(",")
                if not field or field.startswith("--"):
                    continue
                node["items"].append({"name": field, **index.source(path, n)})
                ref = FOREIGN.search(field)
                if ref:
                    fk = re.search(r"FOREIGN\s+KEY\s*\(([^)]+)\)", field, re.I)
                    column = fk[1] if fk else field.split()[0]
                    delayed.append((key, engine + ":" + ref[1], f"{column} → {ref[2]}", path, n))
    for args in delayed:
        index.edge(*args)
    for common in sorted(sql.glob("*.sql")):
        local = sql / "sqlite" / common.name
        path = local if engine == "sqlite" and local.is_file() else common
        if path.name.startswith(("create_", "reset_", "migrate_")):
            continue
        text = index.read(path)
        visited = set()
        for match in re.finditer(r"\b(FROM|JOIN|INTO|UPDATE)\s+(\w+)", text, re.I):
            key = by_name.get(match[2].lower())
            if key and key not in visited:
                visited.add(key)
                index.nodes[key]["queries"].append({"name": path.name, **index.source(path, line_at(text, match.start()))})


def graph(root: Path) -> dict:
    index = SourceIndex(root)
    server = index.root.parent / "RustyFusion"
    index_client(index)
    index_bootstrap(index)
    index_server(index, server)
    for engine in ["sqlite", "postgres"]:
        index_database(index, server, engine)
    ui = "Assets/FFOne/Runtime/UI/"
    client = "Assets/FFOne/Runtime/Network/FusionClient.cs"
    login = "../RustyFusion/src/server/login/login.rs"
    roster = ui + "FrontendSelectionLoading.cs"
    scenarios = []
    for title, landmarks in [
        ("Вход и запрос ростера", [
            ("Форма вызывает LoginAsync", ui + "FrontendController.cs", "await Session.LoginAsync("),
            ("Сборка пакета 0104", client, "Protocol0104.LoginPayload("),
            ("Отправка запроса", client, "login.SendAsync(PacketId.Login,"),
            ("Обработка пакета в RustyFusion", login, "let pkt: &sP_CL2LS_REQ_LOGIN"),
            ("Чтение персонажей аккаунта", login, "db.load_players(acc_id)"),
            ("SQL выборки ростера", "../RustyFusion/sql/load_players.sql", "FROM Players"),
            ("Ответ клиенту", login, "client.send_packet(P_LS2CL_REP_LOGIN_SUCC"),
            ("Открытие ростера", ui + "FrontendController.cs", "await ShowSelectionAsync();\n")
        ]),
        ("Подготовка экрана выбора персонажа", [
            ("Отмена предыдущей загрузки", roster, "selectionLoading?.Cancel()"),
            ("Показ загрузки", roster, "await ShowLoadingAsync("),
            ("Обновление ростера", roster, "RefreshRoster(); Layout()"),
            ("Ожидание предпросмотра", roster, "while (!previews.SelectionReady)"),
            ("Ветка отмены", roster, "while (!previews.SelectionReady)\n                {\n                    cancellation.ThrowIfCancellationRequested()"),
            ("Ветка ошибки предпросмотра", roster, "if (!string.IsNullOrEmpty(previews.SelectionError))"),
            ("Ветка тайм-аута", roster, "throw new TimeoutException("),
            ("Закрытие своей версии загрузки", roster, "HideLoading(); SetBusy(false)")
        ])
    ]:
        steps = []
        for name, rel, fragment in landmarks:
            path = index.root / rel
            text = index.read(path)
            offset = text.find(fragment)
            steps.append({"name": name, "source": index.source(path, line_at(text, offset) +
                          fragment.rstrip("\n").count("\n")) if offset >= 0 else None})
        scenarios.append({"name": title, "steps": steps})
    return {"nodes": list(index.nodes.values()), "edges": list(index.edges.values()),
            "scenarios": scenarios, "missing": sorted(set(index.missing))}
