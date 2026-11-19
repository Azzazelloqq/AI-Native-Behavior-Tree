# Samples

Import samples from **Window > Package Manager > AI-Native Behavior Tree > Samples**.

| Sample | Layer | What it shows |
| --- | --- | --- |
| [Simple Agent](SimpleAgent/README.md) | managed | Start here. `BehaviorTreeAgent`, standard `aibt.unity.*` nodes, one custom `BtAction`, gameplay code writing the blackboard and handling events. Menu **AIBT > Samples > Simple Agent > Create Demo**. |
| [First Agent](FirstAgent/README.md) | native | `NativeTreeAgent`, a Burst node with the short `AIBT.Native` attributes, a generated catalog, an Editor-prepared runtime asset, facts and effects. Menu **AIBT > Samples > First Agent > Create Demo**. |
| [Native Guard](NativeGuard/README.md) | native | A tree built only from package nodes (`aibt.core.*` and the `aibt.native.*` library): a reactive guard on facts, a weapon cooldown shared by two branches, waiting for the game, signals to the game. No Burst code. Menu **AIBT > Samples > Native Guard > Create Demo**. |
| [Swarm Arena](SwarmArena/README.md) | native, low level | One reactive tree for many agents through `ProductionTreeHost` and `ProductionTreeWorld`: patrol, pursuit, danger avoidance, cooldown attack. A gameplay and debugging sample, not a benchmark. |
| [Public Burst Nodes](BurstNodes/README.md) | native | Condition and asynchronous Action nodes with the long public Burst API. |
| [Showcase Trees](ShowcaseTrees/README.md) | either | Readable production-node trees with saved layouts; open them in **AIBT > Graph Editor**. |

`CustomMcpToolProvider`, `FullExample` and `SemanticSlice` are reference material for MCP tool
providers, hot-reload compatibility and the semantic slice; they are not listed in Package Manager.

Samples demonstrate supported APIs; they do not define hidden runtime contracts.
