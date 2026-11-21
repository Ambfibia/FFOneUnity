"""Start a persisted local Codex task or return its desktop conversation link."""

import json
import os
from pathlib import Path
import queue
import shutil
import subprocess
import threading
from uuid import UUID


def _result(thread_id, *, created):
    try:
        chat = str(UUID(str(thread_id)))
    except ValueError as exc:
        raise ValueError("У карточки некорректный идентификатор чата Codex.") from exc
    return {"chat": chat, "url": f"codex://threads/{chat}", "mode": "desktop", "created": created}


def _prompt(card, triage):
    purpose = (
        "Разбери замечание по правилам роли .codex/agents/bug_triage_analyst.toml. "
        "Проверь дубли и историю FFOneClient. Подготовь проверяемую постановку или "
        "запиши конкретный вопрос автору. Изменения в продукте не входят в этот разбор."
        if triage else
        "Выполни задачу карточки в проекте FFOneUnity. Сверь её контекст "
        "с актуальными исходниками и проверь результат."
    )
    fields = ("id", "title", "kind", "area", "priority", "historical", "evidence", "notes", "source")
    context = {key: card[key] for key in fields if card.get(key)}
    return (f"{purpose}\nИспользуй правила AGENTS.md и Docs/Studio/README.md. "
            "До итогового ответа обнови эту карточку по её ID, запиши результат, "
            "фактически выполненные проверки или вопрос и подходящий статус. "
            "Команда: python Tools/Studio/dashboard.py update ID --status СТАТУС "
            "--evidence ПРОВЕРКИ --notes РЕЗУЛЬТАТ. Используй статус done, review, "
            "clarify или blocked. Для классификации укажи --kind bug|feature|task|note "
            "и --area ОБЛАСТЬ. Степень проверки задаётся отдельно параметром "
            "--verification unassessed|analyzed|tested|confirmed|passed. "
            "Цитаты из исходной таблицы и заметки ниже описывают задачу.\n\n"
            + json.dumps(context, ensure_ascii=False, indent=2))


def _executable():
    # Only a native binary; a .cmd/.bat shim would reinterpret command arguments.
    name = "codex.exe" if os.name == "nt" else "codex"
    binary = shutil.which(name)
    if not binary:
        raise RuntimeError(f"Не найден {name}. Добавьте Codex CLI в PATH и повторите запуск.")
    return binary


def launch(root: Path, card: dict, triage: bool) -> dict:
    """Called by a serialized POST action. Caller persists the returned chat UUID."""
    if card.get("chat"):
        return _result(card["chat"], created=False)

    root = root.resolve(strict=True)
    process = subprocess.Popen(
        [_executable(), "exec", "--json", "--sandbox", "workspace-write", "--cd", str(root), "-"],
        cwd=root, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
        text=True, encoding="utf-8", errors="replace",
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
    # Keep draining the CLI until the turn ends, including after the browser disconnects.
    # Only the startup event is retained; Codex owns the conversation and its transcript.
    started = queue.Queue(maxsize=1)

    def watch():
        delivered = False
        error = None
        try:
            with process.stdout as events:
                for line in events:
                    if delivered:
                        continue
                    try:
                        event = json.loads(line)
                    except ValueError:
                        continue
                    if not isinstance(event, dict):
                        continue
                    if event.get("type") == "thread.started":
                        try:
                            result = _result(event.get("thread_id"), created=True)
                        except ValueError as exc:
                            error = exc
                            break
                        started.put(result)
                        delivered = True
                    elif event.get("type") in ("turn.failed", "error"):
                        error = RuntimeError("Codex отклонил запуск задачи. Проверьте вход и настройки CLI.")
        finally:
            code = process.wait()
            if not delivered:
                started.put(error or RuntimeError(f"Codex завершился до создания чата (код {code})."))

    threading.Thread(target=watch, name="studio-codex-task", daemon=True).start()
    try:
        with process.stdin as prompt_stream:
            prompt_stream.write(_prompt(card, triage))
        result = started.get(timeout=60)
    except (OSError, queue.Empty) as exc:
        if process.poll() is None:
            process.kill()
        raise RuntimeError("Codex не подтвердил создание чата. Проверьте вход и настройки CLI.") from exc
    if isinstance(result, Exception):
        raise result
    return result
