# Samples

Start with [`SimpleAgent`](SimpleAgent/README.md): one `BehaviorTreeAgent` component, three small
node classes and a guard that patrols, chases and returns. It needs no generated catalog.

Start with [`ShowcaseTrees`](ShowcaseTrees/README.md): two readable production-node trees with saved
layouts. Open **AIBT > Graph Editor**, then pass either `.aibt.json` path to `OpenFromPath` through
your Editor integration or MCP. The current graph window has no file-picker control.

Additional samples will be added as vertical slices stabilize. Planned coverage includes patrol and
combat, event-driven reactions, async commands, managed integration, scheduling policies, behavior
cases, and MCP authoring. Custom Burst nodes, hot reload, and visual organization now have examples.

[`SwarmArena`](SwarmArena/README.md) is the importable production gameplay showcase: one reactive
tree drives patrol, pursuit, danger avoidance and cooldown attack through public world facts and
typed effects. It is a gameplay/debugging sample, not a benchmark result.

Samples demonstrate supported APIs; they do not define hidden runtime contracts.
