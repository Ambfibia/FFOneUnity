"""Regressions for sheet history, card overlays, and cross-process dashboard updates."""
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import tempfile
import unittest

from dashboard.cards import AUDIT, Board, ROOT


class BoardTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.root = Path(folder.name)
        target = self.root / AUDIT
        target.parent.mkdir(parents=True)
        target.write_bytes((ROOT / AUDIT).read_bytes())
        (target.parent / "provenance.json").write_text('{"entries":[]}')
        self.board = Board(self.root)

    def test_seed_preserves_historical_state(self):
        c = self.board.get("bugs-C58")
        self.assertEqual(c["historical"], "Закрыто на двух машинах")
        self.assertEqual(c["verification"], "tested")
        self.assertEqual(c["status"], "review")
        self.assertEqual(self.board.get("wish-A2")["kind"], "feature")

    def test_card_update_survives_restart_and_preserves_concurrent_fields(self):
        key = self.board.create({"title":"Тест редактора","kind":"task"})["id"]
        with ThreadPoolExecutor(max_workers=3) as pool:
            list(pool.map(lambda fields: Board(self.root).save(key, fields),
                          [{"priority":"high"}, {"notes":"Проверить UI"}, {"area":"Editor"}]))
        card = Board(self.root).get(key)
        self.assertEqual((card["priority"],card["notes"],card["area"]), ("high","Проверить UI","Editor"))
        self.assertEqual(len(card["history"]), 4)

    def test_failed_update_does_not_change_base_or_history(self):
        key = "bugs-C58"
        with self.assertRaises(ValueError):
            self.board.save(key, {"status":"done","evidence":""})
        self.assertEqual(self.board.get(key)["history"], [])
        saved = self.board.save(key, {"notes":"Проверить сервер"})
        self.assertEqual(len(saved["history"]), 1)
        self.assertEqual(self.board.seed[key]["history"], [])
        self.assertEqual(self.board.save(key, {"notes":"Проверить сервер"})["history"], saved["history"])

    def test_client_cannot_overwrite_source_or_chat(self):
        for field in ("source","historical","chat","original"):
            with self.assertRaises(ValueError):
                self.board.save("bugs-C58", {field:"replacement"})
        with self.assertRaises(ValueError):
            self.board.get("../../other")
