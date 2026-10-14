#!/usr/bin/env python3
"""Run the Exporter runner boundary tests through the existing host checks."""
from pathlib import Path
import unittest

TESTS = Path(__file__).resolve().parent / 'ci/runners/tests'
suite = unittest.defaultTestLoader.discover(str(TESTS), pattern='test_*.py')
result = unittest.TextTestRunner(verbosity=1).run(suite)
raise SystemExit(0 if result.wasSuccessful() else 1)
