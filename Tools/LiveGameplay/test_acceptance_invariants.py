"""Reject completion claims assembled from partial or presentation-only evidence."""
from copy import deepcopy
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from summarize_instance_acceptance import require_complete_partition
from write_instance_recall_acceptance import (
    REQUIRED_RECALL_NETWORK, REQUIRED_RECALL_PLAYER, map_capture_paths, require_recall_checks,
)


class AcceptanceInvariantTests(unittest.TestCase):
    def test_partition_rows_cannot_erase_runner_native_or_content_failure(self):
        complete = dict(passed=True, serverStopped=True, contentComplete=True,
                        instanceNetwork=dict(passed=True), instancePlayer=dict(passed=True))
        require_complete_partition(complete)
        for field, value in [('passed', False), ('serverStopped', False), ('error', 'Player process exited unsuccessfully'),
                             ('contentComplete', False), ('contentErrors', {'1': ['missing model']})]:
            with self.subTest(field=field):
                changed = deepcopy(complete); changed[field] = value
                with self.assertRaises(ValueError): require_complete_partition(changed)
        for section in ('instanceNetwork', 'instancePlayer'):
            with self.subTest(section=section):
                changed = deepcopy(complete); changed[section]['passed'] = False
                with self.assertRaises(ValueError): require_complete_partition(changed)

    def test_arbitrary_png_count_does_not_replace_named_capture_of_each_map(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for index in range(151): (root / f'unrelated-{index}.png').write_bytes(b'png')
            matrix = dict(playerProfile=folder)
            with self.assertRaises(AssertionError): map_capture_paths(matrix, {1, 2})
            for map_id in (1, 2): (root / f'instance-{map_id}.png').write_bytes(b'png')
            self.assertEqual({1, 2}, set(map_capture_paths(matrix, {1, 2})))
            aliased = dict(mapCaptures={'1': str(root / 'instance-1.png'), '2': str(root / 'instance-1.png')})
            with self.assertRaises(AssertionError): map_capture_paths(aliased, {1, 2})

    def test_presentation_only_recall_or_missing_power_checks_cannot_pass(self):
        old = dict(exercise=dict(checks=['Recall Point original model route loads']),
                   player=dict(checks=['Recall effect 811 presentation QA']))
        with self.assertRaises(AssertionError): require_recall_checks(old)
        accepted = dict(exercise=dict(checks=sorted(REQUIRED_RECALL_NETWORK)),
                        player=dict(checks=sorted(REQUIRED_RECALL_PLAYER)))
        require_recall_checks(accepted)
        missing_power = deepcopy(accepted)
        missing_power['player']['checks'] = [c for c in missing_power['player']['checks'] if 'resolves self' not in c]
        with self.assertRaises(AssertionError): require_recall_checks(missing_power)


if __name__ == '__main__':
    unittest.main()
