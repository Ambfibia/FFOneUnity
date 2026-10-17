"""Regressions for independent, executable production instance plans."""
from copy import deepcopy
from pathlib import Path
import unittest

import instances

ROOT = Path(__file__).resolve().parents[2]


class InstancePlanTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        output = ROOT.parent / 'RustyFusion/target/unity-gameplay-plan-tests/plan.json'
        output.parent.mkdir(parents=True, exist_ok=True)
        cls.baseline = instances.plan(ROOT.parent / 'RustyFusion/tabledata', output)

    def setUp(self):
        self.plan = deepcopy(self.baseline)
        self.cases = {case['map']: case for case in self.plan['cases']}

    def test_all_151_identities_have_executable_production_routes(self):
        self.assertEqual(151, len(self.cases))
        self.assertEqual({'eligible': 151}, self.plan['dispositions'])
        self.assertEqual([], self.plan['unresolvedMapReferences'])

    def test_mission_preparation_does_not_leak_into_map_145_or_other_routes(self):
        for map_id, case in self.cases.items():
            if map_id in (146, 147, 148):
                self.assertEqual([5222], case['tasks'])
                self.assertIn(case['combatWeapon'], case['items'])
            else:
                self.assertFalse(any(edge.get('beforeMission') or edge.get('afterMission')
                                     for edge in case['entry'] + case['exit']), map_id)
        self.assertNotIn('combatWeapon', self.cases[145])

    def test_changing_one_staged_route_cannot_change_another_case(self):
        action = self.cases[146]['entry'][0]['beforeMission'][0]
        self.assertEqual('equip', action['kind'])
        action['item'] = 2147483647
        self.assertEqual(65, self.cases[147]['entry'][0]['beforeMission'][0]['item'])
        self.assertEqual(65, self.cases[148]['entry'][0]['beforeMission'][0]['item'])
        self.assertNotIn('beforeMission', self.cases[145]['entry'][0])


if __name__ == '__main__':
    unittest.main()
