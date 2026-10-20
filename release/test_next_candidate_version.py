import unittest

from next_candidate_version import choose_next, parse_version


class CandidateVersionTests(unittest.TestCase):
    def test_advances_beyond_existing_candidate(self):
        self.assertEqual(
            "3.2.10",
            choose_next("3.2.8", ["development-tools-candidate-v3.2.9-b49b14c"]),
        )

    def test_stable_version_is_used_when_no_candidate_exists(self):
        self.assertEqual("3.2.9", choose_next("3.2.8", []))

    def test_unrelated_artifacts_are_ignored(self):
        self.assertEqual(
            "3.2.10",
            choose_next("3.2.9", ["agent-bundle-deadbeef", "DevelopmentTools_v99.0.0.zip"]),
        )

    def test_highest_retained_candidate_wins(self):
        self.assertEqual(
            "3.3.2",
            choose_next("3.2.9", [
                "development-tools-candidate-v3.2.10-aaaaaaa",
                "development-tools-candidate-v3.3.1-bbbbbbb",
            ]),
        )

    def test_invalid_stable_version_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "invalid semantic version"):
            parse_version("3.2")


if __name__ == "__main__":
    unittest.main()
