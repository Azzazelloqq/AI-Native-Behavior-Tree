# Simple Agent

A guard patrols four waypoints, chases a wanderer while it can see it, and returns to its route
when the wanderer gets away. Everything runs through one component, `BehaviorTreeAgent`; there is
no generated catalog, runtime asset or host wiring.

## Run it

1. Import **Simple Agent** from AIBT's Package Manager Samples tab.
2. Run **AIBT > Samples > Simple Agent > Create Demo**, then press Play.
3. The blue guard walks its square route. When the orange wanderer enters its sight cone the red
   marker appears and the guard chases; past the lose range it goes back to patrolling.
4. Double-click `Trees/guard.aibt` to open the tree in **AIBT > Graph Editor**.

## What to read

| File | Shows |
| --- | --- |
| `Trees/guard.aibt` | A priority selector: chase while the target is visible, otherwise patrol. |
| `Runtime/GuardNodes.cs` | Three project nodes, each an ordinary class. |
| `Runtime/GuardSenses.cs` | Gameplay code that writes what the guard perceives into the blackboard. |

## How the pieces talk to each other

- **Gameplay to tree.** `GuardSenses` calls `agent.Blackboard.Set("target-visible", ...)` and
  `Set("target-position", ...)` every frame. The tree sees the new values on its next tick.
- **Tree to gameplay.** Nodes act on the GameObject directly: `aibt.unity.move-towards` moves the
  Transform, `samples.simple.set-alert` calls `AlertView.Show`.
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
