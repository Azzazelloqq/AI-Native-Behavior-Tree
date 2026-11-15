import json, os, sys
out = sys.argv[1]
os.makedirs(out, exist_ok=True)
BIG = 4000000000
leaves = {
  "stdlib-wait1": {"type": "aibt.stdlib.wait", "typeVersion": 1, "parameters": {"ticks": 1}},
  "unity-succeed": {"type": "aibt.unity.succeed", "typeVersion": 1},
  "unity-is-true": {"type": "aibt.unity.is-true", "typeVersion": 1, "parameters": {"key": "flag", "expected": False}},
  "unity-is-true-both": {"type": "aibt.unity.is-true", "typeVersion": 1, "parameters": {"key": "flag", "expected": False, "interrupt": "both"}},
  "unity-set-float": {"type": "aibt.unity.set-float", "typeVersion": 1, "parameters": {"key": "f", "value": 1.0}},
  "unity-compare-float": {"type": "aibt.unity.compare-float", "typeVersion": 1, "parameters": {"key": "f", "comparison": "less-than", "value": 5.0}},
}
bb = {"flag": {"type": "Bool", "typeVersion": 1, "scope": "tree", "default": False},
      "f": {"type": "Float32", "typeVersion": 1, "scope": "tree", "default": 0.0}}
for name, leaf in leaves.items():
    for count in (0, 14):
        nodes = {"root": {"type": "aibt.core.reactive-sequence", "typeVersion": 1, "children": ["c%d" % i for i in range(count)] + ["hold"]}}
        for i in range(count): nodes["c%d" % i] = leaf
        nodes["hold"] = {"type": "aibt.stdlib.wait", "typeVersion": 1, "parameters": {"ticks": BIG}}
        doc = {"format": "aibt.tree", "formatVersion": 2, "treeId": "probe.%s.%d" % (name, count), "name": name, "root": "root", "blackboard": bb, "nodes": nodes}
        json.dump(doc, open(os.path.join(out, "%s-%d.aibt.json" % (name, count)), "w"), indent=1)
    # memory-sequence variant: leaves executed once then a long hold (per tick only the hold runs)
