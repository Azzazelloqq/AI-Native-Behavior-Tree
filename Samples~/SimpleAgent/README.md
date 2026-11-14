# Simple Agent

A guard patrols four waypoints, chases a wanderer while it can see it, and returns to its route
when the wanderer gets away. A scout roams near its corner and, while it has line of sight to the
wanderer, keeps three to five meters away from it. Everything runs through one component,
`BehaviorTreeAgent`; there is no generated catalog, runtime asset or host wiring. Almost every node
is a standard `aibt.unity.*` node; one project node is left as the "write your own" example.

## Run it

1. Import **Simple Agent** from AIBT's Package Manager Samples tab.
2. Run **AIBT > Samples > Simple Agent > Create Demo**, then press Play.
3. The blue guard walks its square route and looks around at each corner. When the orange
   wanderer enters its sight cone the red marker appears, the yellow beacon flashes and the guard
   chases; past the lose range it goes back to patrolling. The green scout backs away from the
   wanderer whenever it sees it.
4. Double-click `Trees/guard.aibt` to open the tree in **AIBT > Graph Editor**.

## What to read

| File | Shows |
| --- | --- |
| `Trees/guard.aibt` | A priority selector: chase while the target is visible, otherwise patrol. |
| `Trees/wander.aibt` | Random points around the arena center (`random-point`, `wait-random`). |
| `Trees/scout.aibt` | A named scene reference, `line-of-sight` and `keep-distance`, with no gameplay script. |
| `Runtime/GuardNodes.cs` | The one project node, an ordinary class. |
| `Runtime/GuardSenses.cs` | Gameplay code that writes what the guard perceives into the blackboard. |
| `Runtime/SpottedFlash.cs` | Gameplay code that reacts to the tree's `spotted` event. |

## How the pieces talk to each other

- **Gameplay to tree.** `GuardSenses` calls `agent.Blackboard.Set("target-visible", ...)` and
  `Set("target-position", ...)` every frame. The tree sees the new values on its next tick.
- **Tree to gameplay.** Nodes act on the GameObject directly: `aibt.unity.move-towards` moves the
  Transform, `aibt.unity.set-active` shows the marker named `alert` in the agent's **References**
  list, and `aibt.unity.raise-event` raises `spotted`, which `SpottedFlash` receives through
  `agent.Events.Subscribe("spotted", ...)`. The same event can be wired without code in the
  agent's **Event Responses** list. The chase restarts every time the guard reaches the target,
  so the event is guarded by `aibt.unity.is-cooldown-ready` / `aibt.unity.start-cooldown`: it
  is raised at most once every 3 seconds.
- **Other scene objects.** The blackboard holds values, not objects. A node that needs another
  object takes a reference name: the scout's agent lists the wanderer as `intruder`, and
  `aibt.unity.get-position` copies its position into a key for `line-of-sight` and `keep-distance`.
- **Node to node.** `samples.simple.next-waypoint` writes the `waypoint` key and
  `aibt.unity.move-towards` reads it. Both name the key through a `BbKey<Vector3>` parameter that
  the tree binds.

## Why the chase interrupts the patrol

`Sees target?` has `interrupt: both`. A memory selector normally keeps the child it chose until
that child finishes. With an interrupt mode the condition is re-checked every tick: when it
starts holding, the running patrol is aborted and the selector chooses again; when it stops
holding, the chase is aborted and the selector falls through to patrol. The patrol sequence
itself keeps its place between ticks, so `Look around` really waits.

When the patrol sequence finishes, the tree is done; the agent's **Restart On Complete** option
starts it again, which is what advances the guard to its next waypoint. Blackboard values survive
that restart.

## Write your own node

```csharp
[BtNode("game.heal", Category = "Combat", Summary = "Restores health over time.")]
public sealed class HealNode : BtAction
{
    public float perSecond = 5f;          // a parameter the tree author edits
    public BbKey<float> health;           // a blackboard key the tree author picks

    protected override NodeStatus OnTick()
    {
        var value = Mathf.Min(100f, Get(health) + perSecond * DeltaTime);
        Set(health, value);
        return value >= 100f ? NodeStatus.Success : NodeStatus.Running;
    }
}
```

Override `OnEnter`, `OnAbort` and `OnExit` when the node has something to start or clean up, and
`OnInitialize` to cache components once. Conditions derive from `BtCondition` and override
`Check()`.
