import argparse
import glob
import json
import os
from dataclasses import dataclass, field


@dataclass
class SessionAgg:
    session_id: str
    dda_mode: str = ""
    has_session_end: bool = False
    has_survey: bool = False
    has_survey_skipped: bool = False
    fairness: int | None = None
    continuity: int | None = None
    end_reason: str = ""
    run_elapsed_seconds_end: float | None = None
    survey_comment: str = ""
    ui_trigger: str = ""
    examples: list[str] = field(default_factory=list)


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


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Validate presence of H5/H6 survey events in PostHog JSONL exports."
    )
    ap.add_argument(
        "inputs",
        nargs="+",
        help="JSONL files or globs, e.g. tools/posthog_exports/ALL_events_*.jsonl",
    )
    ap.add_argument(
        "--show-ok",
        action="store_true",
        help="Also print sessions that look OK.",
    )
    args = ap.parse_args()

    files: list[str] = []
    for inp in args.inputs:
        matches = glob.glob(inp)
        files.extend(matches if matches else [inp])
    files = [os.path.normpath(p) for p in files]

    agg: dict[str, SessionAgg] = {}
    bad_lines = 0

    def _extract_ui_trigger(comment: str) -> str:
        if not comment:
            return ""
        marker = "ui_trigger="
        idx = comment.rfind(marker)
        if idx < 0:
            return ""
        return comment[idx + len(marker) :].strip()

    for path in files:
        if not os.path.exists(path):
            print(f"[missing] {path}")
            continue

        for line_no, obj in _iter_jsonl(path):
            if obj is None:
                bad_lines += 1
                continue

            props = obj.get("properties") or {}
            session_id = props.get("session_id")
            if not session_id:
                continue

            a = agg.get(session_id)
            if a is None:
                a = SessionAgg(session_id=session_id)
                agg[session_id] = a

            event = obj.get("event") or ""
            event_kind = props.get("event_kind") or ""

            dda_mode = props.get("dda_mode") or ""
            if dda_mode and not a.dda_mode:
                a.dda_mode = dda_mode

            if event == "dda_session_end" or event_kind == "session_end":
                a.has_session_end = True
                a.end_reason = props.get("end_reason") or a.end_reason
                try:
                    a.run_elapsed_seconds_end = float(props.get("run_elapsed_seconds"))
                except Exception:
                    pass
                if not a.survey_comment and isinstance(props.get("survey_comment"), str):
                    a.survey_comment = props.get("survey_comment") or ""
                if not a.ui_trigger:
                    a.ui_trigger = _extract_ui_trigger(a.survey_comment)

                # session_end duplicates the fields; keep them if present
                if "fairness_likert_1_7" in props and a.fairness is None:
                    try:
                        a.fairness = int(props.get("fairness_likert_1_7"))
                    except Exception:
                        pass
                if "continuity_likert_1_7" in props and a.continuity is None:
                    try:
                        a.continuity = int(props.get("continuity_likert_1_7"))
                    except Exception:
                        pass

            if event == "dda_post_session_survey" or event_kind == "post_session_survey":
                a.has_survey = True
                try:
                    a.fairness = int(props.get("fairness_likert_1_7"))
                except Exception:
                    pass
                try:
                    a.continuity = int(props.get("continuity_likert_1_7"))
                except Exception:
                    pass
                if isinstance(props.get("survey_comment"), str):
                    a.survey_comment = props.get("survey_comment") or a.survey_comment
                if not a.ui_trigger:
                    a.ui_trigger = _extract_ui_trigger(a.survey_comment)

            if event == "dda_post_session_survey_skipped" or event_kind == "post_session_survey_skipped":
                a.has_survey_skipped = True
                if isinstance(props.get("survey_comment"), str):
                    a.survey_comment = props.get("survey_comment") or a.survey_comment
                if not a.ui_trigger:
                    a.ui_trigger = _extract_ui_trigger(a.survey_comment)

            if len(a.examples) < 2 and event in ("dda_session_end", "dda_post_session_survey", "dda_post_session_survey_skipped"):
                a.examples.append(event)

    sessions = list(agg.values())
    sessions.sort(key=lambda s: s.session_id)

    missing_survey = [
        s for s in sessions
        if s.has_session_end and (not s.has_survey) and (not s.has_survey_skipped)
    ]
    missing_session_end = [
        s for s in sessions
        if (s.has_survey or s.has_survey_skipped) and (not s.has_session_end)
    ]
    invalid_survey = [
        s for s in sessions
        if s.has_survey and (s.fairness is None or s.continuity is None or not (1 <= s.fairness <= 7) or not (1 <= s.continuity <= 7))
    ]

    ok = [
        s for s in sessions
        if s.has_session_end and (s.has_survey or s.has_survey_skipped) and s not in invalid_survey
    ]

    print(f"files={len(files)} sessions={len(sessions)} bad_json_lines={bad_lines}")
    print(f"ok_sessions={len(ok)} missing_survey_sessions={len(missing_survey)} missing_session_end_sessions={len(missing_session_end)} invalid_survey_sessions={len(invalid_survey)}")

    if missing_survey:
        print("\n[MISSING survey] session_end exists, but no survey/survey_skipped found:")
        for s in missing_survey:
            reason = s.end_reason or "?"
            trig = s.ui_trigger or "?"
            print(f"- session_id={s.session_id} dda_mode={s.dda_mode or '?'} end_reason={reason} ui_trigger={trig} examples={s.examples}")

    if invalid_survey:
        print("\n[INVALID survey] survey exists, but likert values missing/out of range:")
        for s in invalid_survey:
            trig = s.ui_trigger or "?"
            print(f"- session_id={s.session_id} dda_mode={s.dda_mode or '?'} fairness={s.fairness} continuity={s.continuity} ui_trigger={trig} examples={s.examples}")

    if missing_session_end:
        print("\n[MISSING session_end] survey/survey_skipped exists, but session_end not found:")
        for s in missing_session_end:
            tag = "survey" if s.has_survey else "skipped"
            trig = s.ui_trigger or "?"
            print(f"- session_id={s.session_id} dda_mode={s.dda_mode or '?'} {tag} ui_trigger={trig} examples={s.examples}")

    if args.show_ok and ok:
        print("\n[OK]")
        for s in ok:
            tag = "survey" if s.has_survey else "skipped"
            trig = s.ui_trigger or "?"
            print(f"- session_id={s.session_id} dda_mode={s.dda_mode or '?'} {tag} fairness={s.fairness} continuity={s.continuity} ui_trigger={trig}")

    # Non-zero exit if we found problems
    return 1 if missing_survey or invalid_survey else 0


if __name__ == "__main__":
    raise SystemExit(main())

