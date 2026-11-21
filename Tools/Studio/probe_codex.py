"""Probe installed Codex discovery via its native app-server, without starting a chat."""
import argparse
import json
from pathlib import Path
import queue
import subprocess
import threading
import time

ROOT = Path(__file__).resolve().parents[2]


def probe(executable, timeout=45):
    process = subprocess.Popen([str(executable), "app-server", "--stdio"], cwd=ROOT,
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                               stderr=subprocess.DEVNULL, text=True, encoding="utf-8")
    lines = queue.Queue()
    def reader():
        for line in process.stdout:
            lines.put(line)
    threading.Thread(target=reader, daemon=True).start()
    def request(method, params, identifier):
        process.stdin.write(json.dumps({"id": identifier, "method": method, "params": params}) + "\n")
        process.stdin.flush()
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                raw = lines.get(timeout=min(1, max(0.01, deadline-time.monotonic())))
            except queue.Empty:
                if process.poll() is not None:
                    raise RuntimeError(f"app-server exited {process.returncode}")
                continue
            result = json.loads(raw)
            if result.get("id") == identifier:
                if "error" in result:
                    raise RuntimeError(json.dumps(result["error"]))
                return result["result"]
        raise TimeoutError(f"{method} timed out after {timeout}s")
    try:
        init = request("initialize", {"clientInfo": {"name": "ffone_studio_probe", "version": "1.0"}}, 1)
        process.stdin.write('{"method":"initialized","params":{}}\n')
        process.stdin.flush()
        skills = request("skills/list", {"cwds": [str(ROOT)], "forceReload": True}, 2)
        config = request("config/read", {"cwd": str(ROOT), "includeLayers": True}, 3)
        # Do not persist config values, MCP command arguments or credentials.
        project_skills, errors = [], []
        for row in skills.get("data", []):
            for skill in row.get("skills", []):
                if skill.get("path") and Path(skill["path"]).resolve().is_relative_to(ROOT.resolve()):
                    project_skills.append({key: skill.get(key) for key in ("name", "path", "enabled", "scope")})
            errors.extend(row.get("errors", []))
        layers = [{"name": layer.get("name"), "disabledReason": layer.get("disabledReason")}
                  for layer in config.get("layers", [])]
        return {"client": init, "project_skills": project_skills, "skill_errors": errors,
                "agents_config": config.get("config", {}).get("agents"), "layers": layers,
                "limitation": "Discovery/config probe only. Native delegated tasks are verified separately; no chat or model request was started."}
    finally:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--codex", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = probe(args.codex)
    text = json.dumps(result, indent=2, ensure_ascii=False)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text, encoding="utf-8")
    print(text)
