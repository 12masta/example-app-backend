#!/usr/bin/env python3
"""Fail if Coverlet wrote an empty Cobertura document."""

from __future__ import annotations

import glob
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: assert-cobertura-not-empty.py <glob-or-file>", file=sys.stderr)
        return 2

    pattern = sys.argv[1]
    matches = sorted(Path(p) for p in glob.glob(pattern, recursive=True))
    if not matches:
        print(f"no cobertura files matched {pattern}", file=sys.stderr)
        return 1

    path = matches[-1]
    root = ET.parse(path).getroot()
    valid = int(root.attrib.get("lines-valid", "0"))
    covered = int(root.attrib.get("lines-covered", "0"))
    if valid <= 0:
        print(f"{path} is empty (lines-valid={valid})", file=sys.stderr)
        return 1

    print(f"{path}: {covered}/{valid} lines")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
