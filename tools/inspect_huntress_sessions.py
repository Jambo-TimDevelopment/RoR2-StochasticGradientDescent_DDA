import argparse
import glob
import json
import os
from collections import defaultdict


def _iter_jsonl(path: str):
    with open(path, "r", encoding="utf-8") as f:
        for line_no, line in enumerate(f, start=1):
            line = line.strip()
            if not line:
                continue
            try:
                yield line_no, json.loads(line)
            except Exception:
                yield line_no, None


def _normalize_mojibake(text: str) -> str:
    if not text:
        return ""
    text = str(text)
    markers = ("Ã", "Ð", "Ñ", "Â", "â", "€", "™")
    if not any(m in text for m in markers):
        return text
    try:
        return text.encode("latin-1", errors="strict").decode("utf-8", errors="strict")
    except Exception:
        return text


def _is_huntress(body: str) -> bool:
    t = _normalize_mojibake(body).strip().lower()
    return ("охотниц" in t) or ("huntress" in t)


def _axes_from_props(props: dict) -> list[str]:
    axes = set()
    for k in props.keys():
        if k.startswith("axis_") and k.endswith("_abs_error"):
            axes.add(k[len("axis_") : -len("_abs_error")])
    return sorted(axes)


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Inspect Huntress sessions: list session_ids by mode and check H2/H4 field presence."
    )
    ap.add_argument(
        "inputs",
        nargs="+",
        help="JSONL files or globs, e.g. tools/posthog_exports/ALL_events_*.jsonl",
    )
    args = ap.parse_args()

    files: list[str] = []
    for inp in args.inputs:
        matches = glob.glob(inp)
        files.extend(matches if matches else [inp])
    files = [os.path.normpath(p) for p in files]

    mode: dict[str, str] = {}
    body: dict[str, str] = {}

    # H2 presence
    tau_jump: dict[str, float] = {}
    axis_is_jump_obs = defaultdict(int)
    axis_delta_multiplier_obs = defaultdict(int)

    # H4 presence
    recovery_events = defaultdict(int)
    recovery_seconds = defaultdict(list)
    is_degraded_obs = defaultdict(int)
    is_degraded_true = defaultdict(int)

    for path in files:
        if not os.path.exists(path):
            print(f"[missing] {path}")
            continue
        for _line_no, obj in _iter_jsonl(path):
            if obj is None:
                continue
            props = obj.get("properties") or {}
            session_id = props.get("session_id")
            if not session_id:
                continue

            dda_mode = props.get("dda_mode") or ""
            if dda_mode and session_id not in mode:
                mode[session_id] = dda_mode

            b = props.get("player_body") or props.get("player_body_name") or ""
            if b and session_id not in body:
                body[session_id] = _normalize_mojibake(b)

            if session_id not in tau_jump and props.get("tau_jump") is not None:
                try:
                    tau_jump[session_id] = float(props.get("tau_jump"))
                except Exception:
                    pass

            event = obj.get("event") or ""
            if event == "dda_sample":
                axes = _axes_from_props(props)
                for ax in axes:
                    if f"axis_{ax}_is_jump" in props:
                        axis_is_jump_obs[session_id] += 1
                    if f"axis_{ax}_delta_multiplier" in props:
                        try:
                            float(props.get(f"axis_{ax}_delta_multiplier"))
                            axis_delta_multiplier_obs[session_id] += 1
                        except Exception:
                            pass

                if "is_degraded" in props:
                    is_degraded_obs[session_id] += 1
                    if bool(props.get("is_degraded")):
                        is_degraded_true[session_id] += 1

            if event == "dda_recovery":
                recovery_events[session_id] += 1
                try:
                    recovery_seconds[session_id].append(
                        float(props.get("recovery_elapsed_seconds"))
                    )
                except Exception:
                    pass

    huntress_sessions = sorted([sid for sid, b in body.items() if _is_huntress(b)])
    by_mode = defaultdict(list)
    for sid in huntress_sessions:
        by_mode[mode.get(sid, "")].append(sid)

    print("Huntress sessions by mode:")
    for m in sorted(by_mode.keys()):
        label = m if m else "(unknown)"
        sids = sorted(by_mode[m])
        print(f"- {label}: {len(sids)}")
        for sid in sids:
            print(f"  - {sid}")

    print("\nPer-session H2/H4 presence (Huntress only):")
    for sid in huntress_sessions:
        print(f"\n{sid}")
        print(f"  mode={mode.get(sid, '')}")
        print(f"  player_body={body.get(sid, '')}")
        print(
            "  H2 fields:"
            f" tau_jump={tau_jump.get(sid)}"
            f" axis_*_is_jump_obs={axis_is_jump_obs[sid]}"
            f" axis_*_delta_multiplier_obs={axis_delta_multiplier_obs[sid]}"
        )
        print(
            "  H4 fields:"
            f" recovery_events={recovery_events[sid]}"
            f" recovery_elapsed_seconds={recovery_seconds[sid]}"
        )
        print(
            "  degradation evidence:"
            f" is_degraded_obs={is_degraded_obs[sid]}"
            f" is_degraded_true={is_degraded_true[sid]}"
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

