import unittest

from prepare_release import parse_version, validate_promotion_version


class ReleasePromotionVersionTests(unittest.TestCase):
    def test_accepts_verified_candidate_newer_than_stable(self):
        validate_promotion_version("3.2.8", "3.2.12")

    def test_rejects_same_version(self):
        with self.assertRaisesRegex(ValueError, "must be newer"):
            validate_promotion_version("3.2.8", "3.2.8")

    def test_rejects_older_version(self):
        with self.assertRaisesRegex(ValueError, "must be newer"):
            validate_promotion_version("3.2.8", "3.2.7")

    def test_rejects_invalid_candidate_version(self):
        with self.assertRaisesRegex(ValueError, "major.minor.patch"):
            validate_promotion_version("3.2.8", "3.2")

    def test_rejects_invalid_stable_version(self):
        with self.assertRaisesRegex(ValueError, "major.minor.patch"):
            validate_promotion_version("stable", "3.2.12")

    def test_numeric_comparison_does_not_use_lexical_order(self):
        self.assertGreater(parse_version("3.2.12", "version"), parse_version("3.2.9", "version"))


if __name__ == "__main__":
    unittest.main()
