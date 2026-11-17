"""Generates the benchmark trees used by Assets/AibtPerf (managed and native layers).

shared-*   : only aibt.core composites/decorators and aibt.stdlib leaves; the identical JSON runs
             in a BehaviorTreeAgent and (baked) in a NativeTreeAgent.
unity-*    : managed-only trees built from aibt.unity.* BtNode leaves.
native-*   : native-only trees built from aibt.native.* standard nodes (and the First Agent move node).
"""
import json
import os
import sys

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "Assets", "AibtPerf", "Trees")
BIG = 4000000000


def tree(tree_id, root, nodes, blackboard=None):
    return {
        "format": "aibt.tree",
        "formatVersion": 2,
        "treeId": "aibt.perf." + tree_id,
        "name": tree_id,
        "root": root,
        "blackboard": blackboard or {},
        "nodes": nodes,
    }


def n(type_id, children=None, parameters=None, bindings=None):
    node = {"type": type_id, "typeVersion": 1}
    if children is not None:
        node["children"] = children
    if parameters is not None:
        node["parameters"] = parameters
    if bindings is not None:
        node["bindings"] = bindings
    return node


def rc(pct):
    return n("aibt.stdlib.random-condition", parameters={"success-chance-percent": pct})


def wait(ticks):
    return n("aibt.stdlib.wait", parameters={"ticks": ticks})


def ok():
    # Deterministic "succeeds at once" leaf available in both layers (random-condition is
    # nondeterministic and is rejected by the native compile policy, AIBT2040).
    return wait(1)


def shape15_nodes():
    # Guard-like shape: a memory selector that tries a gated "chase" branch (gate fails) and
    # falls back to a "patrol" sequence with two waits. Completes after ~22 ticks.
    return {
        "priority": n("aibt.core.memory-selector", ["chase", "patrol"]),
        "chase": n("aibt.core.memory-sequence", ["c-gate", "c-wait"]),
        "c-gate": n("aibt.core.failer", ["c-g"]),
        "c-g": ok(),
        "c-wait": wait(5),
        "patrol": n("aibt.core.memory-sequence", ["p-ok1", "p-wait1", "p-ok2", "p-succ", "p-wait2", "p-ok3", "p-ok4"]),
        "p-ok1": ok(),
        "p-wait1": wait(10),
        "p-ok2": ok(),
        "p-succ": n("aibt.core.succeeder", ["p-no"]),
        "p-no": n("aibt.core.failer", ["p-x"]),
        "p-x": ok(),
        "p-wait2": wait(10),
        "p-ok3": ok(),
        "p-ok4": ok(),
    }


def shared_trees():
    out = {}
    out["shared-shape15"] = tree("shared-shape15", "priority", shape15_nodes())
    loop = shape15_nodes()
    loop["loop"] = n("aibt.core.repeater", ["priority"], {"count": BIG, "stopOnFailure": False})
    out["shared-loop16"] = tree("shared-loop16", "loop", loop)
    for total in (2, 16, 64):
        conds = total - 2
        nodes = {"root": n("aibt.core.reactive-sequence", ["c%d" % i for i in range(conds)] + ["hold"])}
        for i in range(conds):
            nodes["c%d" % i] = ok()
        nodes["hold"] = wait(BIG)
        out["shared-reactive%d" % total] = tree("shared-reactive%d" % total, "root", nodes)
    return out


def unity_trees():
    out = {}
    bb = {
        "target-visible": {"type": "Bool", "typeVersion": 1, "scope": "tree", "default": False},
        "alert": {"type": "Float32", "typeVersion": 1, "scope": "tree", "default": 0.0},
        "phase": {"type": "Float32", "typeVersion": 1, "scope": "tree", "default": 0.0},
    }
    for interrupt in ("both", "none"):
        nodes = {
            "priority": n("aibt.core.memory-selector", ["chase", "patrol"]),
            "chase": n("aibt.core.memory-sequence", ["sees", "alert-on", "c-wait"]),
            "sees": n("aibt.unity.is-true", parameters={"key": "target-visible", "interrupt": interrupt}),
            "alert-on": n("aibt.unity.set-float", parameters={"key": "alert", "value": 1.0}),
            "c-wait": n("aibt.unity.wait-frames", parameters={"frames": 5}),
            "patrol": n("aibt.core.memory-sequence", ["alert-off", "step1", "chk1", "wait1", "step2", "chk2", "wait2", "ok", "skip"]),
            "alert-off": n("aibt.unity.set-float", parameters={"key": "alert", "value": 0.0}),
            "step1": n("aibt.unity.set-float", parameters={"key": "phase", "value": 1.0}),
            "chk1": n("aibt.unity.compare-float", parameters={"key": "phase", "comparison": "greater-or-equal", "value": 1.0}),
            "wait1": n("aibt.unity.wait-frames", parameters={"frames": 10}),
            "step2": n("aibt.unity.set-float", parameters={"key": "phase", "value": 2.0}),
            "chk2": n("aibt.unity.compare-float", parameters={"key": "phase", "comparison": "greater-or-equal", "value": 2.0}),
            "wait2": n("aibt.unity.wait-frames", parameters={"frames": 10}),
            "ok": n("aibt.unity.succeed"),
            "skip": n("aibt.unity.succeed"),
        }
        out["unity-shape15-" + interrupt] = tree("unity-shape15-" + interrupt, "priority", nodes, bb)
    return out


def native_trees():
    out = {}
    bb = {
        "target-visible": {"type": "Bool", "typeVersion": 1, "scope": "tree", "default": False},
        "alert": {"type": "Float32", "typeVersion": 1, "scope": "tree", "default": 0.0},
        "phase": {"type": "Float32", "typeVersion": 1, "scope": "tree", "default": 0.0},
    }
    nodes = {
        "priority": n("aibt.core.memory-selector", ["chase", "patrol"]),
        "chase": n("aibt.core.memory-sequence", ["sees", "alert-on", "c-wait"]),
        "sees": n("aibt.native.bool-is-true", bindings={"key": "target-visible"}),
        "alert-on": n("aibt.native.set-float", parameters={"value": 1.0}, bindings={"key": "alert"}),
        "c-wait": wait(5),
        "patrol": n("aibt.core.memory-sequence", ["alert-off", "step1", "chk1", "wait1", "step2", "chk2", "wait2", "ok", "skip"]),
        "alert-off": n("aibt.native.set-float", parameters={"value": 0.0}, bindings={"key": "alert"}),
        "step1": n("aibt.native.set-float", parameters={"value": 1.0}, bindings={"key": "phase"}),
        "chk1": n("aibt.native.float-at-least", parameters={"threshold": 1.0}, bindings={"key": "phase"}),
        "wait1": wait(10),
        "step2": n("aibt.native.set-float", parameters={"value": 2.0}, bindings={"key": "phase"}),
        "chk2": n("aibt.native.float-at-least", parameters={"threshold": 2.0}, bindings={"key": "phase"}),
        "wait2": wait(10),
        "ok": ok(),
        "skip": ok(),
    }
    out["native-shape15"] = tree("native-shape15", "priority", nodes, bb)
    # IO trees: one leaf. "move" reads the distance fact and emits the move-speed effect every tick
    # while distance > stop-distance (default 100 keeps it running forever).
    io_bb = {"distance": {"type": "Float32", "typeVersion": 1, "scope": "tree", "default": 100.0}}
    out["native-move1"] = tree("native-move1", "move", {
        "move": n("aibt.samples.first-agent.move", parameters={"speed": 1.5, "stop-distance": 0.25}, bindings={"distance": "distance"}),
    }, io_bb)
    out["native-wait1"] = tree("native-wait1", "hold", {"hold": wait(BIG)})
    out.update(io_trees())
    out.update(act_trees())
    return out


def io_trees():
    # P10-010 (simulation container stage G2): the G1 `sim` trees with game inputs and commands.
    # io-reactive16 = shared-reactive16 with its first condition reading the "sense" input row and
    # its held leaf giving an order (one command) on every think while it keeps running.
    # io-loop16 = shared-loop16 with patrol's first step reading the input and its third step
    # giving one order (a command per patrol pass, not per think).
    out = {}
    sense = n("aibt.perf.sense-at-least", parameters={"threshold": 0.0})
    nodes = {"root": n("aibt.core.reactive-sequence", ["sense"] + ["c%d" % i for i in range(13)] + ["hold"])}
    nodes["sense"] = sense
    for i in range(13):
        nodes["c%d" % i] = ok()
    nodes["hold"] = n("aibt.perf.order-hold")
    out["io-reactive16"] = tree("io-reactive16", "root", nodes)
    loop = shape15_nodes()
    loop["p-ok1"] = n("aibt.perf.sense-at-least", parameters={"threshold": 0.0})
    loop["p-ok3"] = n("aibt.perf.order-once")
    loop["loop"] = n("aibt.core.repeater", ["priority"], {"count": BIG, "stopOnFailure": False})
    out["io-loop16"] = tree("io-loop16", "loop", loop)
    return out


def act_trees():
    # P10-011 (simulation container stage G3): the io-* twins with an external action.
    # act-reactive16 = io-reactive16 with the held leaf replaced by a repeater around aibt.perf.job.
    # The root reactive sequence aborts its running child at every update, so the job is cancelled
    # and restarted at every think and never consumes a completion (P10-011 finding F2).
    # act-loop16 = io-loop16 with its order step replaced by the job: start, poll, complete, order.
    out = {}
    nodes = {"root": n("aibt.core.reactive-sequence", ["sense"] + ["c%d" % i for i in range(13)] + ["jobs"])}
    nodes["sense"] = n("aibt.perf.sense-at-least", parameters={"threshold": 0.0})
    for i in range(13):
        nodes["c%d" % i] = ok()
    nodes["jobs"] = n("aibt.core.repeater", ["job"], {"count": BIG, "stopOnFailure": False})
    nodes["job"] = n("aibt.perf.job")
    out["act-reactive16"] = tree("act-reactive16", "root", nodes)
    loop = shape15_nodes()
    loop["p-ok1"] = n("aibt.perf.sense-at-least", parameters={"threshold": 0.0})
    loop["p-ok3"] = n("aibt.perf.job")
    loop["loop"] = n("aibt.core.repeater", ["priority"], {"count": BIG, "stopOnFailure": False})
    out["act-loop16"] = tree("act-loop16", "loop", loop)
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    trees = {}
    trees.update(shared_trees())
    trees.update(unity_trees())
    trees.update(native_trees())
    for name, doc in trees.items():
        with open(os.path.join(OUT, name + ".aibt.json"), "w", encoding="utf-8", newline="\n") as handle:
            json.dump(doc, handle, indent=2)
            handle.write("\n")
    print("\n".join(sorted(trees)))


if __name__ == "__main__":
    main()
