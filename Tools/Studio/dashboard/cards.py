"""Dashboard records. The sheet audit is the baseline, Git stores one overlay per card."""
from contextlib import contextmanager
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[3]
AUDIT = "Docs/Studio/ffoneclient-unity-triage-2026-11-18.md"
ENUMS = {
    "kind": ("bug", "feature", "note", "task"),
    "status": ("new", "clarify", "ready", "active", "review", "done", "blocked"),
    "verification": ("unassessed", "analyzed", "tested", "confirmed", "passed"),
    "priority": ("low", "normal", "high"),
}
EDITABLE = {"title", "notes", "evidence", "area", *ENUMS}
TESTED = {"C17", "C33", "C34", "C58", "C95", "C136", "G2", "G3"}
ROW = re.compile(r"^\| \[([A-Z]+\d+)\]\((https://docs\.google\.com/[^ )]+)\) \| (.*) \|$")

# Only suggest a subsystem when the sheet itself gives none. The analyst can revise it.
TOPICS = [
    ("Аудио", r"звук|аудио|озвуч|музык|громкост|голос"),
    ("Управление", r"геймпад|автобег|клавиш|d-pad|стик|нажат|бинд"),
    ("Графика и анимации", r"текстур|шейдер|анимаци|меш\b|свечени|мерца|коллиз|коллизи|колайдер"),
    ("Социальные функции", r"друг(?:ого|ой|их) игрок|обмен|приглашени|групп|почт|чат"),
    ("Квесты и прогресс", r"квест|мисси|журнал|наград|уровн"),
    ("Транспорт", r"транспорт|зиплайн|vehicle|обезьян|скампер|скайвей|слайдер"),
    ("Интерфейс", r"меню|кнопк|интерфейс|иконк|миникарт|мини-карт|вкладк"),
    ("Nano", r"нано|\bnano"),
    ("Персонажи", r"персонаж|ростер|непис|нпс|\bnpc"),
    ("Бой", r"моб|стрел|выстрел|оруж|здоровь|враг|урон"),
]


def defaults():
    return dict(kind="note", status="new", priority="normal", verification="unassessed",
                area="Входящие", notes="", evidence="", source="", historical="", chat="",
                triageChat="", history=[], updated="")


def baseline(root):
    records = {}
    section = ""
    for line in (root / AUDIT).read_text(encoding="utf-8-sig").splitlines():
        if line.startswith("## "):
            section = line[3:]
        match = ROW.match(line)
        if not match:
            continue
        cell, source, values = match.groups()
        columns = [v.replace(r"\|", "|").replace(" ⏎ ", "\n") for v in values.split(" | ")]
        wish = "gid=1328021982&" in source
        title, category, *tail = columns
        key = ("wish-" if wish else "bugs-") + cell
        if key in records or len(columns) != (2 if wish else 3):
            raise ValueError(f"Некорректная строка реестра {key}")
        evidence = "" if wish else tail[0]
        reviewed = bool(evidence and not evidence.startswith("Не проверено"))
        area = category if wish else section
        if section.startswith("Закрыто") or section == "Открыто":
            area = next((name for name, pattern in TOPICS if re.search(pattern, title, re.I)),
                        "Без подсистемы")
        records[key] = dict(defaults(), id=key, title=title, original=title, source=source,
            kind="feature" if wish or cell.startswith("D") else "bug", area=area,
            historical="" if wish else category, sourceSection=section,
            evidence="" if evidence == "Не проверено" else evidence,
            status="review" if reviewed else "new",
            verification=("tested" if cell in TESTED else "analyzed") if reviewed else "unassessed")
    if not records:
        raise ValueError("Нет строк в исходном реестре")
    return records


@contextmanager
def lock(root, key, timeout=15):
    folder = root / "Logs/Studio/dashboard-locks"
    folder.mkdir(parents=True, exist_ok=True)
    path = folder / (key + ".lock")
    with path.open("a+b") as handle:
        if not path.stat().st_size:
            handle.write(b"0")
            handle.flush()
        deadline = time.monotonic() + timeout
        while True:
            handle.seek(0)
            try:
                if os.name == "nt":
                    import msvcrt
                    msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except OSError:
                if time.monotonic() >= deadline:
                    raise TimeoutError("Карточка занята другим обновлением")
                time.sleep(.1)
        yield


class Board:
    def __init__(self, root=ROOT):
        self.root = root
        self.folder = root / "Docs/Studio/dashboard/cards"
        self.seed = baseline(root)

    def path(self, key):
        if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9_-]{0,75}", key):
            raise ValueError("Некорректный ID")
        return self.folder / (key + ".json")

    def get(self, key):
        path = self.path(key)
        overlay = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
        if key not in self.seed and not overlay:
            raise KeyError(key)
        card = dict(defaults() | self.seed.get(key, {}) | overlay, id=key)
        card["history"] = list(card["history"])
        return card

    def state(self):
        # Only overlays need disk access. The source register is immutable during a session.
        changed = {p.stem for p in self.folder.glob("*.json")}
        keys = dict.fromkeys([*sorted(changed), *self.seed])
        research = json.loads((self.root / "Docs/Studio/provenance.json").read_text(encoding="utf-8"))
        return {"cards": [self.get(k) if k in changed else self.seed[k] for k in keys],
                "research": [dict(title=e["name"], status=e["status"],
                    source="/source?file=Docs/Studio/provenance.json", id=e["id"],
                    notes=e.get("unity", "")) for e in research["entries"]]}

    def save(self, key, fields, *, create=False, internal=False):
        if not isinstance(fields, dict) or not fields:
            raise ValueError("Нет полей для записи")
        allowed = EDITABLE | ({"chat", "triageChat"} if internal else set())
        if fields.keys() - allowed:
            raise ValueError("Неизвестное поле карточки")
        for name, value in fields.items():
            if not isinstance(value, str) or len(value) > 32000:
                raise ValueError(f"Некорректное значение {name}")
            if name in ENUMS and value not in ENUMS[name]:
                raise ValueError(f"Недопустимое значение {name}")
            if name in {"title", "area"} and not value.strip():
                raise ValueError(f"Поле {name} пустое")
        if create and not fields.get("title", "").strip():
            raise ValueError("Введите описание")
        path = self.path(key)
        with lock(self.root, "card-" + key):
            if create:
                if path.exists() or key in self.seed:
                    raise ValueError("Карточка уже существует")
                card = dict(defaults(), id=key, original=fields["title"])
            else:
                card = self.get(key)
            if internal and "chat" in fields and not card["chat"] and card["status"] in ("new", "ready"):
                fields = dict(fields, status="active")
            updates = {k:v for k,v in fields.items() if create or card.get(k) != v}
            if not updates:
                return card
            card.update(updates)
            if card["status"] == "done" and not card["evidence"].strip():
                raise ValueError("Для завершения запишите результат проверки")
            stamp = datetime.now(timezone.utc).isoformat(timespec="seconds")
            card["updated"] = stamp
            card["history"].append({"time": stamp, "changes": updates})
            self.folder.mkdir(parents=True, exist_ok=True)
            data = {k:v for k,v in card.items() if k not in self.seed.get(key, {}) or self.seed[key][k] != v}
            fd, temp = tempfile.mkstemp(dir=self.folder, suffix=".tmp")
            try:
                with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
                    json.dump(data, f, ensure_ascii=False, indent=2)
                    f.write("\n")
                os.replace(temp, path)
            finally:
                Path(temp).unlink(missing_ok=True)
            return card

    def create(self, fields):
        return self.save("studio-" + uuid.uuid4().hex[:12], fields, create=True)
