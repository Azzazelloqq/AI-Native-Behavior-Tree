#!/usr/bin/env python3
"""Rebuild the P7-024 derived data and SVG from committed benchmark JSON."""

from __future__ import annotations

import argparse
import hashlib
import json
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
EVIDENCE = Path(__file__).resolve().parent
SOURCES = {
    "Windows x64 Player": ROOT
    / "Benchmarks~/Phase4/Platform/Windows/Results/windows-player-scheduling-20260821.json",
    "Android ARM64 Player": ROOT
    / "Benchmarks~/Phase4/Platform/Android/Results/android-player-scheduling-20260821.json",
}
AUTO_SOURCE = (
    ROOT
    / "Benchmarks~/Phase4/AutoComparison/Results/auto-comparison-windows-editor-20260821.json"
)
SCENARIO_LABELS = {
    "scheduling-baseline-empty-job": "Baseline leaf",
    "shallow-tree-cheap-conditions": "Shallow sequence",
    "deep-sequence-selector-traversal": "Deep traversal",
    "wide-branching-frequent-failures": "Wide failures",
    "predominantly-running-actions": "Running actions",
    "many-programs-small-populations": "Many-program placeholder",
}
COLORS = ["#2563eb", "#0f766e", "#dc2626", "#9333ea", "#d97706", "#475569"]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def relative(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


def load_player_points() -> tuple[list[dict], dict]:
    points: list[dict] = []
    source_metadata: dict = {}
    for platform, path in SOURCES.items():
        document = json.loads(path.read_text(encoding="utf-8"))
        median_key = (
            "medianNanosecondsPerAgent"
            if platform == "Windows x64 Player"
            else "medianNsPerAgent"
        )
        source_metadata[platform] = {
            "path": relative(path),
            "sha256": sha256(path),
            "schema": document["schema"],
            "unityVersion": document["environment"]["unityVersion"]
            if "environment" in document
            else document["unityVersion"],
        }
        for scenario in document["scenarios"]:
            grouped: dict[int, dict[str, float]] = {}
            for case in scenario["cases"]:
                grouped.setdefault(case["agentCount"], {})[case["policy"]] = case[
                    median_key
                ]
            for agent_count, policies in sorted(grouped.items()):
                required = {"Immediate", "Budgeted", "BatchedJobsSameFrame"}
                if set(policies) != required:
                    raise ValueError(
                        f"{path}: expected {sorted(required)}, got {sorted(policies)}"
                    )
                fastest = min(policies["Immediate"], policies["Budgeted"])
                points.append(
                    {
                        "platform": platform,
                        "scenario": scenario["name"],
                        "agentCount": agent_count,
                        "immediateMedianNsPerAgent": policies["Immediate"],
                        "budgetedMedianNsPerAgent": policies["Budgeted"],
                        "batchedJobsSameFrameMedianNsPerAgent": policies[
                            "BatchedJobsSameFrame"
                        ],
                        "fastestNonJobsMedianNsPerAgent": fastest,
                        "batchedToFastestNonJobsRatio": round(
                            policies["BatchedJobsSameFrame"] / fastest, 4
                        ),
                    }
                )
    return points, source_metadata


def load_auto_summary() -> dict:
    document = json.loads(AUTO_SOURCE.read_text(encoding="utf-8"))
    cases = [case for scenario in document["scenarios"] for case in scenario["cases"]]
    underperforming = [case for case in cases if case["outcome"] == "Underperforms"]
    jobs_cases = [
        case for case in underperforming if case["autoChosenPolicy"] == "BatchedJobsSameFrame"
    ]
    return {
        "source": {
            "path": relative(AUTO_SOURCE),
            "sha256": sha256(AUTO_SOURCE),
            "schema": document["schema"],
        },
        "caseCount": len(cases),
        "underperformingCaseCount": len(underperforming),
        "matchingOrBetterCaseCount": len(cases) - len(underperforming),
        "confidenceValues": sorted({case["autoConfidence"] for case in cases}),
        "jobsSelectionUnderperformanceGapPercent": {
            "minimum": round(min(case["gapPercent"] for case in jobs_cases), 4),
            "maximum": round(max(case["gapPercent"] for case in jobs_cases), 4),
        },
    }


def build_data() -> dict:
    points, sources = load_player_points()
    return {
        "schema": "aibt-p7-024-showcase-derived-v1",
        "metric": (
            "BatchedJobsSameFrame median nanoseconds per agent divided by the lower of "
            "Immediate and Budgeted median nanoseconds per agent for the same platform, "
            "scenario, and population."
        ),
        "rounding": "Ratios are rounded to four decimal places after division.",
        "playerSources": sources,
        "playerPoints": points,
        "autoEditorSummary": load_auto_summary(),
    }


def esc(value: str) -> str:
    return (
        value.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


def build_svg(data: dict) -> str:
    width, height = 1240, 720
    panel_width, panel_height = 500, 420
    panel_top = 145
    panel_lefts = [100, 700]
    y_min, y_max = 10.0, 32.0
    platforms = ["Windows x64 Player", "Android ARM64 Player"]
    rows = [
        '<svg xmlns="http://www.w3.org/2000/svg" width="1240" height="720" viewBox="0 0 1240 720">',
        '<rect width="1240" height="720" fill="#ffffff"/>',
        '<style>text{font-family:Inter,Segoe UI,Arial,sans-serif;fill:#172033}.title{font-size:26px;font-weight:700}.subtitle{font-size:15px;fill:#526174}.axis{font-size:13px;fill:#526174}.panel{font-size:18px;font-weight:650}.legend{font-size:13px}</style>',
        '<text x="620" y="42" text-anchor="middle" class="title">Same-frame Jobs cost relative to the fastest plain loop</text>',
        '<text x="620" y="70" text-anchor="middle" class="subtitle">Median ns/agent; lower is better. Every point is one committed Player scenario.</text>',
    ]

    for panel_index, platform in enumerate(platforms):
        left = panel_lefts[panel_index]
        platform_points = [p for p in data["playerPoints"] if p["platform"] == platform]
        counts = sorted({p["agentCount"] for p in platform_points})
        rows.append(
            f'<text x="{left + panel_width / 2:.0f}" y="112" text-anchor="middle" class="panel">{esc(platform)}</text>'
        )
        for tick in [10, 15, 20, 25, 30]:
            y = panel_top + panel_height * (y_max - tick) / (y_max - y_min)
            rows.append(
                f'<line x1="{left}" y1="{y:.1f}" x2="{left + panel_width}" y2="{y:.1f}" stroke="#dbe2ea" stroke-width="1"/>'
            )
            rows.append(
                f'<text x="{left - 12}" y="{y + 5:.1f}" text-anchor="end" class="axis">{tick}x</text>'
            )
        rows.append(
            f'<line x1="{left}" y1="{panel_top + panel_height}" x2="{left + panel_width}" y2="{panel_top + panel_height}" stroke="#7b8794"/>'
        )
        rows.append(
            f'<line x1="{left}" y1="{panel_top}" x2="{left}" y2="{panel_top + panel_height}" stroke="#7b8794"/>'
        )
        for index, count in enumerate(counts):
            x = left + panel_width * index / max(1, len(counts) - 1)
            rows.append(
                f'<text x="{x:.1f}" y="{panel_top + panel_height + 26}" text-anchor="middle" class="axis">{count}</text>'
            )
        rows.append(
            f'<text x="{left + panel_width / 2:.0f}" y="{panel_top + panel_height + 55}" text-anchor="middle" class="axis">Agent count</text>'
        )

        for scenario_index, (scenario, label) in enumerate(SCENARIO_LABELS.items()):
            scenario_points = sorted(
                [p for p in platform_points if p["scenario"] == scenario],
                key=lambda p: p["agentCount"],
            )
            coordinates = []
            for point in scenario_points:
                x_index = counts.index(point["agentCount"])
                x = left + panel_width * x_index / max(1, len(counts) - 1)
                ratio = point["batchedToFastestNonJobsRatio"]
                if not y_min <= ratio <= y_max:
                    raise ValueError(f"ratio {ratio} outside chart bounds")
                y = panel_top + panel_height * (y_max - ratio) / (y_max - y_min)
                coordinates.append((x, y))
            path = " ".join(
                ("M" if index == 0 else "L") + f" {x:.1f} {y:.1f}"
                for index, (x, y) in enumerate(coordinates)
            )
            color = COLORS[scenario_index]
            rows.append(
                f'<path d="{path}" fill="none" stroke="{color}" stroke-width="2.5" opacity="0.9"/>'
            )
            for x, y in coordinates:
                rows.append(
                    f'<circle cx="{x:.1f}" cy="{y:.1f}" r="4" fill="{color}" stroke="#ffffff" stroke-width="1.5"/>'
                )

    legend_y = 645
    for index, label in enumerate(SCENARIO_LABELS.values()):
        column = index % 3
        row = index // 3
        x = 105 + column * 385
        y = legend_y + row * 27
        rows.append(
            f'<line x1="{x}" y1="{y}" x2="{x + 24}" y2="{y}" '
            f'stroke="{COLORS[index]}" stroke-width="3"/>'
        )
        rows.append(f'<circle cx="{x + 12}" cy="{y}" r="4" fill="{COLORS[index]}"/>')
        rows.append(f'<text x="{x + 34}" y="{y + 5}" class="legend">{esc(label)}</text>')
    rows.append('</svg>')
    return "\n".join(rows) + "\n"


def write_outputs(directory: Path) -> None:
    data = build_data()
    (directory / "derived-data.json").write_text(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    (directory / "jobs-vs-non-jobs.svg").write_text(build_svg(data), encoding="utf-8")


def check_outputs() -> None:
    with tempfile.TemporaryDirectory() as temp:
        candidate = Path(temp)
        write_outputs(candidate)
        mismatches = []
        for name in ["derived-data.json", "jobs-vs-non-jobs.svg"]:
            expected = EVIDENCE / name
            actual = candidate / name
            if not expected.exists() or expected.read_bytes() != actual.read_bytes():
                mismatches.append(name)
        if mismatches:
            raise SystemExit("generated outputs differ: " + ", ".join(mismatches))
    print("P7-024 generated artifacts match committed benchmark JSON.")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if args.check:
        check_outputs()
    else:
        write_outputs(EVIDENCE)
        print("Generated derived-data.json and jobs-vs-non-jobs.svg")


if __name__ == "__main__":
    main()
