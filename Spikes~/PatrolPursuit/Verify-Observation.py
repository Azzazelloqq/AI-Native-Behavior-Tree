"""Check this controlled research scene's expected behavior, independently of its BT nodes."""
import json
import math
import pathlib
import sys

report_path = pathlib.Path(sys.argv[1])
report = json.loads(report_path.read_text(encoding="utf-8"))
assert report["elapsed"] >= 45
assert report["worldFailure"] == "None"
assert report["allHostsRunning"] and report["alarmConsistent"]
assert report["completedCycle"]
assert report["wanderArrivals"] >= 2 and report["wanderDistance"] > 1

events = [event for event in report["events"] if event["actor"] == "Patroller"]
assert [event["mode"] for event in events[:4]] == ["PATROL", "CHASE", "RETURN", "PATROL"]
assert events[1]["target"] == events[2]["target"] == 1
assert events[1]["distance"] <= 5.21
assert events[2]["distance"] > 9
assert math.hypot(events[3]["position"]["x"] + 5, events[3]["position"]["z"] - 3) <= 0.21
for event in events:
    if event["mode"] == "CHASE":
        assert event["target"] in (1, 2) and event["distance"] <= 5.21
    if event["mode"] == "RETURN":
        assert event["distance"] > 9

assert len(report["samples"]) >= 45
for sample in report["samples"]:
    assert sample["alarm"] == (sample["mode"] == "CHASE")
    assert sample["failures"] == ["None"] * 3
    assert all(value.startswith("NoTerminalResult; updates=") for value in sample["roots"])
    for name in ("patrol", "runner", "wander"):
        assert -12 <= sample[name]["x"] <= 12 and -8 <= sample[name]["z"] <= 8

print(json.dumps({
    "verified": True,
    "seconds": report["elapsed"],
    "firstDetectionSeconds": events[1]["time"],
    "firstLossSeconds": events[2]["time"],
    "firstLossDistance": events[2]["distance"],
    "firstPatrolResumeSeconds": events[3]["time"],
    "returnTransitions": sum(event["mode"] == "RETURN" for event in events),
    "wanderArrivals": report["wanderArrivals"],
    "alarmConsistent": True,
    "nativeFailures": 0,
}, indent=2))
