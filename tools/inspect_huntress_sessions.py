"""
Deprecated entry point: use inspect_dda_sessions.py.

Per-survivor filtering (e.g. Huntress-only) is not part of the research workflow;
telemetry is analyzed across the full roster unless you filter externally.
"""

import runpy
import sys
from pathlib import Path

if __name__ == "__main__":
    target = Path(__file__).with_name("inspect_dda_sessions.py")
    sys.argv[0] = str(target)
    runpy.run_path(str(target), run_name="__main__")
