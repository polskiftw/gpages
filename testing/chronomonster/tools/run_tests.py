#!/usr/bin/env python3
"""Dependency-free runner for the pytest-style suite.

The suite also runs under real pytest. This runner exists so a fresh Windows
machine can validate the core before installing optional developer packages.
"""
from __future__ import annotations

import importlib
import inspect
import re
import sys
import tempfile
import traceback
import types
from pathlib import Path


class Raises:
    def __init__(self, expected, match=None): self.expected, self.match = expected, match
    def __enter__(self): return self
    def __exit__(self, kind, value, tb):
        if kind is None: raise AssertionError(f"Expected {self.expected.__name__}")
        if not issubclass(kind, self.expected): return False
        if self.match and not re.search(self.match, str(value)): raise AssertionError(f"Exception {value!r} does not match {self.match!r}")
        return True


class SkipMark:
    def __init__(self, condition, reason): self.condition, self.reason = condition, reason
    def __call__(self, target):
        target.__skip__ = self.condition; target.__skip_reason__ = self.reason
        return target


fake = types.ModuleType("pytest")
fake.raises = lambda expected, match=None: Raises(expected, match)
fake.mark = types.SimpleNamespace(skipif=lambda condition, reason="": SkipMark(condition, reason))
sys.modules.setdefault("pytest", fake)


def main():
    root = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(root))
    passed = failed = skipped = 0
    for module_name in ("tests.test_core", "tests.test_ffmpeg_integration"):
        module = importlib.import_module(module_name)
        module_mark = getattr(module, "pytestmark", None)
        if getattr(module_mark, "condition", False):
            count = sum(1 for name, fn in inspect.getmembers(module, inspect.isfunction) if name.startswith("test_"))
            print(f"SKIP {module_name}: {module_mark.reason}")
            skipped += count
            continue
        for name, fn in inspect.getmembers(module, inspect.isfunction):
            if not name.startswith("test_"): continue
            label = f"{module_name}.{name}"
            if getattr(fn, "__skip__", False):
                print(f"SKIP {label}: {getattr(fn, '__skip_reason__', '')}"); skipped += 1; continue
            try:
                params = inspect.signature(fn).parameters
                if "tmp_path" in params:
                    with tempfile.TemporaryDirectory() as td: fn(tmp_path=Path(td))
                else: fn()
                print(f"PASS {label}"); passed += 1
            except Exception:
                print(f"FAIL {label}"); traceback.print_exc(); failed += 1
    print(f"\nRESULT: {passed} passed, {failed} failed, {skipped} skipped")
    return 1 if failed else 0


if __name__ == "__main__": raise SystemExit(main())

