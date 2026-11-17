# AIBT node catalog (generated)

Source: the real AIBT node registry, via `AIBT.Authoring.NodeCatalogQuery` (P6-003). Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command. Do not hand-edit -- edits are overwritten on the next regeneration.

67 registered node type(s).

---

### `aibt.core.cooldown` (v1)

- **Summary:** Block child entry until the per-instance cooldown deadline has passed.
- **Category:** Cooldown
- **Kind:** Decorator
- **When to use:** Use to rate-limit child activation without a tree blackboard key.
- **When not to use:** Do not use when cooldown state must be shared across tree instances.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Low

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.cooldown",
  "version": 1,
  "summary": "Block child entry until the per-instance cooldown deadline has passed.",
  "category": "Cooldown",
  "kind": "decorator",
  "whenToUse": "Use to rate-limit child activation without a tree blackboard key.",
  "whenNotToUse": "Do not use when cooldown state must be shared across tree instances.",
  "parameters": {
    "blockedResult": {
      "type": "string-enum",
      "required": true,
      "allowedValues": [
        "failure",
        "success"
      ],
      "packing": {
        "offset": 8,
        "size": 1,
        "alignment": 1
      }
    },
    "durationMicroseconds": {
      "type": "uint64",
      "required": true,
      "minimum": 1,
      "packing": {
        "offset": 0,
        "size": 8,
        "alignment": 8
      }
    },
    "startPolicy": {
      "type": "string-enum",
      "required": true,
      "allowedValues": [
        "on-enter",
        "on-successful-exit"
      ],
      "packing": {
        "offset": 9,
        "size": 1,
        "alignment": 1
      }
    }
  },
  "childPolicy": {
    "minimum": 1,
    "maximum": 1,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 8,
    "alignment": 8,
    "lifetime": "instance"
  },
  "configuration": {
    "size": 16,
    "alignment": 8
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "low",
  "examples": [
    {
      "title": "Base behavior",
      "parameters": {
        "blockedResult": "failure",
        "durationMicroseconds": 1000000,
        "startPolicy": "on-enter"
      },
      "expectedBehavior": "A blocked activation fails until one second after child entry."
    }
  ]
}
```

---

### `aibt.core.failer` (v1)

- **Summary:** Convert either terminal child result to failure while preserving running.
- **Category:** Failer
- **Kind:** Decorator
- **When to use:** Use when child completion must force a fallback path.
- **When not to use:** Do not use to discard a success that callers need to observe.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Low

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.failer",
  "version": 1,
  "summary": "Convert either terminal child result to failure while preserving running.",
  "category": "Failer",
  "kind": "decorator",
  "whenToUse": "Use when child completion must force a fallback path.",
  "whenNotToUse": "Do not use to discard a success that callers need to observe.",
  "parameters": {},
  "childPolicy": {
    "minimum": 1,
    "maximum": 1,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "low",
  "examples": [
    {
      "title": "Base behavior",
      "parameters": {},
      "expectedBehavior": "A successful child makes the failer fail."
    }
  ]
}
```

---

### `aibt.core.inverter` (v1)

- **Summary:** Invert terminal success and failure while preserving running.
- **Category:** Inverter
- **Kind:** Decorator
- **When to use:** Use to negate a child's terminal meaning.
- **When not to use:** Do not use when either terminal result must be preserved.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Low

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.inverter",
  "version": 1,
  "summary": "Invert terminal success and failure while preserving running.",
  "category": "Inverter",
  "kind": "decorator",
  "whenToUse": "Use to negate a child's terminal meaning.",
  "whenNotToUse": "Do not use when either terminal result must be preserved.",
  "parameters": {},
  "childPolicy": {
    "minimum": 1,
    "maximum": 1,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "low",
  "examples": [
    {
      "title": "Base behavior",
      "parameters": {},
      "expectedBehavior": "A successful child makes the inverter fail."
    }
  ]
}
```

---

### `aibt.core.memory-selector` (v1)

- **Summary:** Run ordered children until one succeeds or runs, retaining the running child.
- **Category:** Memory selector
- **Kind:** Composite
- **When to use:** Use for fallback behavior whose earlier failed choices need not be reevaluated.
- **When not to use:** Do not use when a higher-priority choice must preempt a running lower-priority choice.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Low

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.memory-selector",
  "version": 1,
  "summary": "Run ordered children until one succeeds or runs, retaining the running child.",
  "category": "Memory selector",
  "kind": "composite",
  "whenToUse": "Use for fallback behavior whose earlier failed choices need not be reevaluated.",
  "whenNotToUse": "Do not use when a higher-priority choice must preempt a running lower-priority choice.",
  "parameters": {},
  "childPolicy": {
    "minimum": 0,
    "maximum": null,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 4,
    "alignment": 4,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "low",
  "examples": [
    {
      "title": "Base behavior",
      "parameters": {},
      "expectedBehavior": "An empty memory selector fails."
    }
  ]
}
```

---

### `aibt.core.memory-sequence` (v1)

- **Summary:** Run ordered children until one fails or runs, retaining the running child.
- **Category:** Memory sequence
- **Kind:** Composite
- **When to use:** Use for ordered multi-step behavior whose completed steps should not be reevaluated.
- **When not to use:** Do not use when earlier conditions must be checked on every update.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Low

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.memory-sequence",
  "version": 1,
  "summary": "Run ordered children until one fails or runs, retaining the running child.",
  "category": "Memory sequence",
  "kind": "composite",
  "whenToUse": "Use for ordered multi-step behavior whose completed steps should not be reevaluated.",
  "whenNotToUse": "Do not use when earlier conditions must be checked on every update.",
  "parameters": {},
  "childPolicy": {
    "minimum": 0,
    "maximum": null,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 4,
    "alignment": 4,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "low",
  "examples": [
    {
      "title": "Base behavior",
      "parameters": {},
      "expectedBehavior": "An empty memory sequence succeeds."
    }
  ]
}
```

---

### `aibt.core.parallel` (v1)

- **Summary:** Visit each non-terminal child in order and complete according to an explicit policy.
- **Category:** Parallel
- **Kind:** Composite
- **When to use:** Use when several child behaviors must progress during the same activation.
- **When not to use:** Do not use to imply simultaneous execution or worker-thread concurrency.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Medium

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.parallel",
  "version": 1,
  "summary": "Visit each non-terminal child in order and complete according to an explicit policy.",
  "category": "Parallel",
  "kind": "composite",
  "whenToUse": "Use when several child behaviors must progress during the same activation.",
  "whenNotToUse": "Do not use to imply simultaneous execution or worker-thread concurrency.",
  "parameters": {
    "failureThreshold": {
      "type": "uint32",
      "required": false,
      "minimum": 1,
      "requiredWhen": {
        "parameter": "policy",
        "equals": "threshold"
      },
      "forbiddenUnless": {
        "parameter": "policy",
        "equals": "threshold"
      },
      "packing": {
        "offset": 8,
        "size": 4,
        "alignment": 4
      }
    },
    "policy": {
      "type": "string-enum",
      "required": true,
      "allowedValues": [
        "require-all-success",
        "require-any-success",
        "threshold"
      ],
      "packing": {
        "offset": 0,
        "size": 1,
        "alignment": 1
      }
    },
    "successThreshold": {
      "type": "uint32",
      "required": false,
      "minimum": 1,
      "requiredWhen": {
        "parameter": "policy",
        "equals": "threshold"
      },
      "forbiddenUnless": {
        "parameter": "policy",
        "equals": "threshold"
      },
      "packing": {
        "offset": 4,
        "size": 4,
        "alignment": 4
      }
    },
    "tieBreak": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "failure-first",
        "success-first"
      ],
      "requiredWhen": {
        "parameter": "policy",
        "equals": "threshold"
      },
      "forbiddenUnless": {
        "parameter": "policy",
        "equals": "threshold"
      },
      "packing": {
        "offset": 12,
        "size": 1,
        "alignment": 1
      }
    }
  },
  "childPolicy": {
    "minimum": 1,
    "maximum": null,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 8,
    "alignment": 4,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 16,
    "alignment": 4
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "medium",
  "examples": [
    {
      "title": "Require all",
      "parameters": {
        "policy": "require-all-success"
      },
      "expectedBehavior": "Succeeds after every child succeeds and fails after any child fails."
    }
  ]
}
```

---

### `aibt.core.reactive-selector` (v1)

- **Summary:** Reevaluate ordered choices from the highest priority on every eligible update.
- **Category:** Reactive selector
- **Kind:** Composite
- **When to use:** Use for priority behavior in which earlier choices can replace a running later choice.
- **When not to use:** Do not use when the chosen branch must run without priority reevaluation.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Low

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.reactive-selector",
  "version": 1,
  "summary": "Reevaluate ordered choices from the highest priority on every eligible update.",
  "category": "Reactive selector",
  "kind": "composite",
  "whenToUse": "Use for priority behavior in which earlier choices can replace a running later choice.",
  "whenNotToUse": "Do not use when the chosen branch must run without priority reevaluation.",
  "parameters": {},
  "childPolicy": {
    "minimum": 0,
    "maximum": null,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 4,
    "alignment": 4,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "low",
  "examples": [
    {
      "title": "Base behavior",
      "parameters": {},
      "expectedBehavior": "An empty reactive selector fails."
    }
  ]
}
```

---

### `aibt.core.reactive-selector-retaining` (v1)

- **Summary:** Recheck higher-priority choices on every eligible update and keep the running choice unless one of them succeeds or runs.
- **Category:** Reactive selector (keeps running child)
- **Kind:** Composite
- **When to use:** Use for priority behavior in which a higher-priority choice preempts long-running work only when it actually succeeds or runs.
- **When not to use:** Do not put a leaf whose Enter or Tick starts expensive or non-idempotent work before the running choice: it runs again on every update.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Low

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.reactive-selector-retaining",
  "version": 1,
  "summary": "Recheck higher-priority choices on every eligible update and keep the running choice unless one of them succeeds or runs.",
  "category": "Reactive selector (keeps running child)",
  "kind": "composite",
  "whenToUse": "Use for priority behavior in which a higher-priority choice preempts long-running work only when it actually succeeds or runs.",
  "whenNotToUse": "Do not put a leaf whose Enter or Tick starts expensive or non-idempotent work before the running choice: it runs again on every update.",
  "parameters": {},
  "childPolicy": {
    "minimum": 0,
    "maximum": null,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 8,
    "alignment": 4,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "low",
  "examples": [
    {
      "title": "Base behavior",
      "parameters": {},
      "expectedBehavior": "An empty selector that keeps its running child fails."
    }
  ]
}
```

---

### `aibt.core.reactive-sequence` (v1)

- **Summary:** Reevaluate ordered children from the first child on every eligible update.
- **Category:** Reactive sequence
- **Kind:** Composite
- **When to use:** Use when earlier conditions must continuously guard a later action.
- **When not to use:** Do not use when restarting the selected running branch is undesirable.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Low

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.reactive-sequence",
  "version": 1,
  "summary": "Reevaluate ordered children from the first child on every eligible update.",
  "category": "Reactive sequence",
  "kind": "composite",
  "whenToUse": "Use when earlier conditions must continuously guard a later action.",
  "whenNotToUse": "Do not use when restarting the selected running branch is undesirable.",
  "parameters": {},
  "childPolicy": {
    "minimum": 0,
    "maximum": null,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 4,
    "alignment": 4,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "low",
  "examples": [
    {
      "title": "Base behavior",
      "parameters": {},
      "expectedBehavior": "An empty reactive sequence succeeds."
    }
  ]
}
```

---

### `aibt.core.reactive-sequence-retaining` (v1)

- **Summary:** Recheck the children before the running one on every eligible update and keep the running child unless one of them fails or runs.
- **Category:** Reactive sequence (keeps running child)
- **Kind:** Composite
- **When to use:** Use when conditions must guard long-running work, such as a multi-update action, that must not restart on every update.
- **When not to use:** Do not put several multi-update children in one sequence: every child before the running one is rechecked and takes over when it runs; group the steps in one memory sequence.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Low

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.reactive-sequence-retaining",
  "version": 1,
  "summary": "Recheck the children before the running one on every eligible update and keep the running child unless one of them fails or runs.",
  "category": "Reactive sequence (keeps running child)",
  "kind": "composite",
  "whenToUse": "Use when conditions must guard long-running work, such as a multi-update action, that must not restart on every update.",
  "whenNotToUse": "Do not put several multi-update children in one sequence: every child before the running one is rechecked and takes over when it runs; group the steps in one memory sequence.",
  "parameters": {},
  "childPolicy": {
    "minimum": 0,
    "maximum": null,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 8,
    "alignment": 4,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "low",
  "examples": [
    {
      "title": "Base behavior",
      "parameters": {},
      "expectedBehavior": "An empty sequence that keeps its running child succeeds."
    }
  ]
}
```

---

### `aibt.core.repeater` (v1)

- **Summary:** Run a child for a positive finite number of complete iterations.
- **Category:** Repeater
- **Kind:** Decorator
- **When to use:** Use for bounded repetition with explicit failure behavior.
- **When not to use:** Do not use for an unbounded loop.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Low

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.repeater",
  "version": 1,
  "summary": "Run a child for a positive finite number of complete iterations.",
  "category": "Repeater",
  "kind": "decorator",
  "whenToUse": "Use for bounded repetition with explicit failure behavior.",
  "whenNotToUse": "Do not use for an unbounded loop.",
  "parameters": {
    "count": {
      "type": "uint32",
      "required": true,
      "minimum": 1,
      "packing": {
        "offset": 0,
        "size": 4,
        "alignment": 4
      }
    },
    "stopOnFailure": {
      "type": "boolean",
      "required": true,
      "packing": {
        "offset": 4,
        "size": 1,
        "alignment": 1
      }
    }
  },
  "childPolicy": {
    "minimum": 1,
    "maximum": 1,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 4,
    "alignment": 4,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 8,
    "alignment": 4
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "low",
  "examples": [
    {
      "title": "Base behavior",
      "parameters": {
        "count": 3,
        "stopOnFailure": true
      },
      "expectedBehavior": "Three successful child iterations complete with success."
    }
  ]
}
```

---

### `aibt.core.succeeder` (v1)

- **Summary:** Convert either terminal child result to success while preserving running.
- **Category:** Succeeder
- **Kind:** Decorator
- **When to use:** Use when child completion, rather than its terminal result, is what matters.
- **When not to use:** Do not use to hide a failure that callers need to handle.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Low

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.succeeder",
  "version": 1,
  "summary": "Convert either terminal child result to success while preserving running.",
  "category": "Succeeder",
  "kind": "decorator",
  "whenToUse": "Use when child completion, rather than its terminal result, is what matters.",
  "whenNotToUse": "Do not use to hide a failure that callers need to handle.",
  "parameters": {},
  "childPolicy": {
    "minimum": 1,
    "maximum": 1,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "low",
  "examples": [
    {
      "title": "Base behavior",
      "parameters": {},
      "expectedBehavior": "A failed child makes the succeeder succeed."
    }
  ]
}
```

---

### `aibt.core.timeout` (v1)

- **Summary:** Abort a running child at a positive injected-clock deadline.
- **Category:** Timeout
- **Kind:** Decorator
- **When to use:** Use to bound how long a child activation may remain running.
- **When not to use:** Do not use wall-clock time directly or a zero duration.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Low

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.core.timeout",
  "version": 1,
  "summary": "Abort a running child at a positive injected-clock deadline.",
  "category": "Timeout",
  "kind": "decorator",
  "whenToUse": "Use to bound how long a child activation may remain running.",
  "whenNotToUse": "Do not use wall-clock time directly or a zero duration.",
  "parameters": {
    "durationMicroseconds": {
      "type": "uint64",
      "required": true,
      "minimum": 1,
      "packing": {
        "offset": 0,
        "size": 8,
        "alignment": 8
      }
    },
    "terminalResult": {
      "type": "string-enum",
      "required": true,
      "allowedValues": [
        "failure",
        "success"
      ],
      "packing": {
        "offset": 8,
        "size": 1,
        "alignment": 1
      }
    }
  },
  "childPolicy": {
    "minimum": 1,
    "maximum": 1,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 8,
    "alignment": 8,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 16,
    "alignment": 8
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "low",
  "examples": [
    {
      "title": "Base behavior",
      "parameters": {
        "durationMicroseconds": 1000000,
        "terminalResult": "failure"
      },
      "expectedBehavior": "After one second the running child is aborted and the decorator fails."
    }
  ]
}
```

---

### `aibt.stdlib.random-condition` (v1)

- **Summary:** Succeeds with a configured percentage probability, drawn from the per-instance random stream.
- **Category:** Core/Conditions
- **Kind:** Condition
- **When to use:** Use for a lightweight probabilistic gate.
- **When not to use:** Do not use when the same tree instance must reproduce an identical outcome sequence on the reference executor -- its managed System.Random draw is not bit-identical to this native stream.
- **Execution domain:** Burst
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Trivial

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.stdlib.random-condition",
  "version": 1,
  "summary": "Succeeds with a configured percentage probability, drawn from the per-instance random stream.",
  "category": "Core/Conditions",
  "kind": "condition",
  "whenToUse": "Use for a lightweight probabilistic gate.",
  "whenNotToUse": "Do not use when the same tree instance must reproduce an identical outcome sequence on the reference executor -- its managed System.Random draw is not bit-identical to this native stream.",
  "parameters": {
    "success-chance-percent": {
      "type": "uint32",
      "required": true,
      "packing": {
        "offset": 0,
        "size": 4,
        "alignment": 4
      }
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 4,
    "alignment": 4
  },
  "cancellation": "not-applicable",
  "executionDomain": "burst",
  "deterministic": false,
  "costHint": "trivial",
  "examples": [
    {
      "title": "stdlib-random-condition",
      "parameters": {
        "success-chance-percent": 0
      },
      "expectedBehavior": "Succeeds with a configured percentage probability, drawn from the per-instance random stream."
    }
  ]
}
```

---

### `aibt.stdlib.wait` (v1)

- **Summary:** Remains running for a configured number of ticks, then succeeds.
- **Category:** Core/Actions
- **Kind:** Action
- **When to use:** Use for a simple tick-counted delay that does not depend on wall-clock time.
- **When not to use:** Do not use when a wall-clock duration is required -- use aibt.core.timeout/aibt.core.cooldown instead.
- **Execution domain:** Burst
- **Deterministic:** True
- **Cancellation:** AbortOnly
- **Cost hint:** Trivial

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.stdlib.wait",
  "version": 1,
  "summary": "Remains running for a configured number of ticks, then succeeds.",
  "category": "Core/Actions",
  "kind": "action",
  "whenToUse": "Use for a simple tick-counted delay that does not depend on wall-clock time.",
  "whenNotToUse": "Do not use when a wall-clock duration is required -- use aibt.core.timeout/aibt.core.cooldown instead.",
  "parameters": {
    "ticks": {
      "type": "uint32",
      "required": true,
      "packing": {
        "offset": 0,
        "size": 4,
        "alignment": 4
      }
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "running",
    "success"
  ],
  "memory": {
    "size": 4,
    "alignment": 4,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 4,
    "alignment": 4
  },
  "cancellation": "abort-only",
  "executionDomain": "burst",
  "deterministic": true,
  "costHint": "trivial",
  "examples": [
    {
      "title": "stdlib-wait",
      "parameters": {
        "ticks": 0
      },
      "expectedBehavior": "Remains running for a configured number of ticks, then succeeds."
    }
  ]
}
```

---

### `aibt.unity.animator-bool` (v1)

- **Summary:** Sets an Animator bool parameter and succeeds; fails when there is no Animator.
- **Category:** Animation
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.animator-bool",
  "version": 1,
  "summary": "Sets an Animator bool parameter and succeeds; fails when there is no Animator.",
  "category": "Animation",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "parameter": {
      "type": "string",
      "required": false,
      "default": "Moving"
    },
    "reference": {
      "type": "string",
      "required": false,
      "default": ""
    },
    "value": {
      "type": "boolean",
      "required": false,
      "default": true
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Animator bool node",
      "parameters": {
        "parameter": "Moving",
        "reference": "",
        "value": true
      },
      "expectedBehavior": "Sets an Animator bool parameter and succeeds; fails when there is no Animator."
    }
  ]
}
```

---

### `aibt.unity.animator-float` (v1)

- **Summary:** Sets an Animator float parameter from a constant or a float key and succeeds; fails when there is no Animator.
- **Category:** Animation
- **Kind:** Action
- **When to use:** Bind from-key to drive a blend tree from the blackboard, for example 'speed'.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.animator-float",
  "version": 1,
  "summary": "Sets an Animator float parameter from a constant or a float key and succeeds; fails when there is no Animator.",
  "category": "Animation",
  "kind": "action",
  "whenToUse": "Bind from-key to drive a blend tree from the blackboard, for example 'speed'.",
  "whenNotToUse": "",
  "parameters": {
    "from-key": {
      "type": "blackboard-key",
      "required": false,
      "valueType": "Float32"
    },
    "parameter": {
      "type": "string",
      "required": false,
      "default": "Speed"
    },
    "reference": {
      "type": "string",
      "required": false,
      "default": ""
    },
    "value": {
      "type": "float32",
      "required": false,
      "default": 0.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Animator float node",
      "parameters": {
        "from-key": "from-key",
        "parameter": "Speed",
        "reference": "",
        "value": 0
      },
      "expectedBehavior": "Sets an Animator float parameter from a constant or a float key and succeeds; fails when there is no Animator."
    }
  ]
}
```

---

### `aibt.unity.animator-trigger` (v1)

- **Summary:** Sets an Animator trigger and succeeds; fails when there is no Animator.
- **Category:** Animation
- **Kind:** Action
- **When to use:** Use to play a one-shot animation such as 'Attack'. The Animator is found on the referenced object or its children.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.animator-trigger",
  "version": 1,
  "summary": "Sets an Animator trigger and succeeds; fails when there is no Animator.",
  "category": "Animation",
  "kind": "action",
  "whenToUse": "Use to play a one-shot animation such as 'Attack'. The Animator is found on the referenced object or its children.",
  "whenNotToUse": "",
  "parameters": {
    "parameter": {
      "type": "string",
      "required": false,
      "default": "Trigger"
    },
    "reference": {
      "type": "string",
      "required": false,
      "default": ""
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Animator trigger node",
      "parameters": {
        "parameter": "Trigger",
        "reference": ""
      },
      "expectedBehavior": "Sets an Animator trigger and succeeds; fails when there is no Animator."
    }
  ]
}
```

---

### `aibt.unity.compare-float` (v1)

- **Summary:** Compares a float blackboard value with a constant.
- **Category:** Blackboard
- **Kind:** Condition
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.compare-float",
  "version": 1,
  "summary": "Compares a float blackboard value with a constant.",
  "category": "Blackboard",
  "kind": "condition",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "comparison": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "equal",
        "greater-or-equal",
        "greater-than",
        "less-or-equal",
        "less-than",
        "not-equal"
      ],
      "default": "less-than"
    },
    "interrupt": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "both",
        "lower-priority",
        "none",
        "self"
      ],
      "default": "none"
    },
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float32"
    },
    "value": {
      "type": "float32",
      "required": false,
      "default": 0.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "not-applicable",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Compare float node",
      "parameters": {
        "comparison": "less-than",
        "interrupt": "none",
        "key": "key",
        "value": 0
      },
      "expectedBehavior": "Compares a float blackboard value with a constant."
    }
  ]
}
```

---

### `aibt.unity.compare-float-keys` (v1)

- **Summary:** Compares two float blackboard values.
- **Category:** Blackboard
- **Kind:** Condition
- **When to use:** Use when both sides change at runtime, such as 'health' against 'flee-health'.
- **When not to use:** Use aibt.unity.compare-float when one side is a constant.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.compare-float-keys",
  "version": 1,
  "summary": "Compares two float blackboard values.",
  "category": "Blackboard",
  "kind": "condition",
  "whenToUse": "Use when both sides change at runtime, such as 'health' against 'flee-health'.",
  "whenNotToUse": "Use aibt.unity.compare-float when one side is a constant.",
  "parameters": {
    "comparison": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "equal",
        "greater-or-equal",
        "greater-than",
        "less-or-equal",
        "less-than",
        "not-equal"
      ],
      "default": "less-than"
    },
    "interrupt": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "both",
        "lower-priority",
        "none",
        "self"
      ],
      "default": "none"
    },
    "left": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float32"
    },
    "right": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float32"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "not-applicable",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Compare float keys node",
      "parameters": {
        "comparison": "less-than",
        "interrupt": "none",
        "left": "left",
        "right": "right"
      },
      "expectedBehavior": "Compares two float blackboard values."
    }
  ]
}
```

---

### `aibt.unity.compare-int` (v1)

- **Summary:** Compares an int blackboard value with a constant.
- **Category:** Blackboard
- **Kind:** Condition
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.compare-int",
  "version": 1,
  "summary": "Compares an int blackboard value with a constant.",
  "category": "Blackboard",
  "kind": "condition",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "comparison": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "equal",
        "greater-or-equal",
        "greater-than",
        "less-or-equal",
        "less-than",
        "not-equal"
      ],
      "default": "equal"
    },
    "interrupt": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "both",
        "lower-priority",
        "none",
        "self"
      ],
      "default": "none"
    },
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Int32"
    },
    "value": {
      "type": "int32",
      "required": false,
      "default": 0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "not-applicable",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Compare int node",
      "parameters": {
        "comparison": "equal",
        "interrupt": "none",
        "key": "key",
        "value": 0
      },
      "expectedBehavior": "Compares an int blackboard value with a constant."
    }
  ]
}
```

---

### `aibt.unity.compare-int-keys` (v1)

- **Summary:** Compares two int blackboard values.
- **Category:** Blackboard
- **Kind:** Condition
- **When to use:** Use when both sides change at runtime, such as 'ammo' against 'ammo-needed'.
- **When not to use:** Use aibt.unity.compare-int when one side is a constant.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.compare-int-keys",
  "version": 1,
  "summary": "Compares two int blackboard values.",
  "category": "Blackboard",
  "kind": "condition",
  "whenToUse": "Use when both sides change at runtime, such as 'ammo' against 'ammo-needed'.",
  "whenNotToUse": "Use aibt.unity.compare-int when one side is a constant.",
  "parameters": {
    "comparison": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "equal",
        "greater-or-equal",
        "greater-than",
        "less-or-equal",
        "less-than",
        "not-equal"
      ],
      "default": "greater-or-equal"
    },
    "interrupt": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "both",
        "lower-priority",
        "none",
        "self"
      ],
      "default": "none"
    },
    "left": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Int32"
    },
    "right": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Int32"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "not-applicable",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Compare int keys node",
      "parameters": {
        "comparison": "greater-or-equal",
        "interrupt": "none",
        "left": "left",
        "right": "right"
      },
      "expectedBehavior": "Compares two int blackboard values."
    }
  ]
}
```

---

### `aibt.unity.copy-bool` (v1)

- **Summary:** Copies one bool blackboard value into another key and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.copy-bool",
  "version": 1,
  "summary": "Copies one bool blackboard value into another key and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "from": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Bool"
    },
    "to": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Bool"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Copy bool node",
      "parameters": {
        "from": "from",
        "to": "to"
      },
      "expectedBehavior": "Copies one bool blackboard value into another key and succeeds."
    }
  ]
}
```

---

### `aibt.unity.copy-float` (v1)

- **Summary:** Copies one float blackboard value into another key and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.copy-float",
  "version": 1,
  "summary": "Copies one float blackboard value into another key and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "from": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float32"
    },
    "to": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float32"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Copy float node",
      "parameters": {
        "from": "from",
        "to": "to"
      },
      "expectedBehavior": "Copies one float blackboard value into another key and succeeds."
    }
  ]
}
```

---

### `aibt.unity.copy-int` (v1)

- **Summary:** Copies one int blackboard value into another key and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.copy-int",
  "version": 1,
  "summary": "Copies one int blackboard value into another key and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "from": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Int32"
    },
    "to": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Int32"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Copy int node",
      "parameters": {
        "from": "from",
        "to": "to"
      },
      "expectedBehavior": "Copies one int blackboard value into another key and succeeds."
    }
  ]
}
```

---

### `aibt.unity.copy-vector3` (v1)

- **Summary:** Copies one Vector3 blackboard value into another key and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **When to use:** Use to remember a position, for example the last place the player was seen.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.copy-vector3",
  "version": 1,
  "summary": "Copies one Vector3 blackboard value into another key and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "Use to remember a position, for example the last place the player was seen.",
  "whenNotToUse": "",
  "parameters": {
    "from": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    },
    "to": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Copy vector3 node",
      "parameters": {
        "from": "from",
        "to": "to"
      },
      "expectedBehavior": "Copies one Vector3 blackboard value into another key and succeeds."
    }
  ]
}
```

---

### `aibt.unity.destroy-self` (v1)

- **Summary:** Destroys the agent's GameObject after an optional delay and succeeds.
- **Category:** GameObject
- **Kind:** Action
- **When to use:** Use at the end of a death or despawn sequence. Running nodes get their exit callbacks when the object is destroyed.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.destroy-self",
  "version": 1,
  "summary": "Destroys the agent's GameObject after an optional delay and succeeds.",
  "category": "GameObject",
  "kind": "action",
  "whenToUse": "Use at the end of a death or despawn sequence. Running nodes get their exit callbacks when the object is destroyed.",
  "whenNotToUse": "",
  "parameters": {
    "delay": {
      "type": "float32",
      "required": false,
      "default": 0.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Destroy self node",
      "parameters": {
        "delay": 0
      },
      "expectedBehavior": "Destroys the agent's GameObject after an optional delay and succeeds."
    }
  ]
}
```

---

### `aibt.unity.distance` (v1)

- **Summary:** Writes the distance between two Vector3 blackboard values to a float key and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **When to use:** Use when later nodes need the number itself, for example to compare it with a key that changes.
- **When not to use:** Use aibt.unity.is-within-distance to just test the agent's own distance to a position.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.distance",
  "version": 1,
  "summary": "Writes the distance between two Vector3 blackboard values to a float key and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "Use when later nodes need the number itself, for example to compare it with a key that changes.",
  "whenNotToUse": "Use aibt.unity.is-within-distance to just test the agent's own distance to a position.",
  "parameters": {
    "flat": {
      "type": "boolean",
      "required": false,
      "default": false
    },
    "from": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    },
    "result": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float32"
    },
    "to": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Distance node",
      "parameters": {
        "flat": false,
        "from": "from",
        "result": "result",
        "to": "to"
      },
      "expectedBehavior": "Writes the distance between two Vector3 blackboard values to a float key and succeeds."
    }
  ]
}
```

---

### `aibt.unity.fail` (v1)

- **Summary:** Fails immediately.
- **Category:** Flow
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.fail",
  "version": 1,
  "summary": "Fails immediately.",
  "category": "Flow",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {},
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Fail node",
      "parameters": {},
      "expectedBehavior": "Fails immediately."
    }
  ]
}
```

---

### `aibt.unity.get-position` (v1)

- **Summary:** Writes the position of the agent or a referenced object to a Vector3 key and succeeds; fails when the reference is missing.
- **Category:** GameObject
- **Kind:** Action
- **When to use:** Use to bring a scene object into the blackboard, for example the 'player' reference before move-towards or look-at.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.get-position",
  "version": 1,
  "summary": "Writes the position of the agent or a referenced object to a Vector3 key and succeeds; fails when the reference is missing.",
  "category": "GameObject",
  "kind": "action",
  "whenToUse": "Use to bring a scene object into the blackboard, for example the 'player' reference before move-towards or look-at.",
  "whenNotToUse": "",
  "parameters": {
    "reference": {
      "type": "string",
      "required": false,
      "default": ""
    },
    "result": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Get position node",
      "parameters": {
        "reference": "",
        "result": "result"
      },
      "expectedBehavior": "Writes the position of the agent or a referenced object to a Vector3 key and succeeds; fails when the reference is missing."
    }
  ]
}
```

---

### `aibt.unity.has-reference` (v1)

- **Summary:** Succeeds while a named reference is assigned, not destroyed and, optionally, active.
- **Category:** GameObject
- **Kind:** Condition
- **When to use:** Use to stop chasing a target that died or was disabled.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.has-reference",
  "version": 1,
  "summary": "Succeeds while a named reference is assigned, not destroyed and, optionally, active.",
  "category": "GameObject",
  "kind": "condition",
  "whenToUse": "Use to stop chasing a target that died or was disabled.",
  "whenNotToUse": "",
  "parameters": {
    "interrupt": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "both",
        "lower-priority",
        "none",
        "self"
      ],
      "default": "none"
    },
    "reference": {
      "type": "string",
      "required": false,
      "default": "target"
    },
    "require-active": {
      "type": "boolean",
      "required": false,
      "default": true
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "not-applicable",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Has reference node",
      "parameters": {
        "interrupt": "none",
        "reference": "target",
        "require-active": true
      },
      "expectedBehavior": "Succeeds while a named reference is assigned, not destroyed and, optionally, active."
    }
  ]
}
```

---

### `aibt.unity.idle` (v1)

- **Summary:** Stays running until a parent interrupts it.
- **Category:** Flow
- **Kind:** Action
- **When to use:** Use as the last child of a reactive selector so the tree keeps running while nothing else applies.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.idle",
  "version": 1,
  "summary": "Stays running until a parent interrupts it.",
  "category": "Flow",
  "kind": "action",
  "whenToUse": "Use as the last child of a reactive selector so the tree keeps running while nothing else applies.",
  "whenNotToUse": "",
  "parameters": {},
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Idle node",
      "parameters": {},
      "expectedBehavior": "Stays running until a parent interrupts it."
    }
  ]
}
```

---

### `aibt.unity.increment-float` (v1)

- **Summary:** Adds an amount to a float blackboard value and succeeds. A negative amount decrements.
- **Category:** Blackboard
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.increment-float",
  "version": 1,
  "summary": "Adds an amount to a float blackboard value and succeeds. A negative amount decrements.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "amount": {
      "type": "float32",
      "required": false,
      "default": 1.0
    },
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float32"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Increment float node",
      "parameters": {
        "amount": 1,
        "key": "key"
      },
      "expectedBehavior": "Adds an amount to a float blackboard value and succeeds. A negative amount decrements."
    }
  ]
}
```

---

### `aibt.unity.increment-int` (v1)

- **Summary:** Adds an amount (default 1) to an int blackboard value and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **When to use:** Use for counters such as patrol index or attempts. A negative amount decrements.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.increment-int",
  "version": 1,
  "summary": "Adds an amount (default 1) to an int blackboard value and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "Use for counters such as patrol index or attempts. A negative amount decrements.",
  "whenNotToUse": "",
  "parameters": {
    "amount": {
      "type": "int32",
      "required": false,
      "default": 1
    },
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Int32"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Increment int node",
      "parameters": {
        "amount": 1,
        "key": "key"
      },
      "expectedBehavior": "Adds an amount (default 1) to an int blackboard value and succeeds."
    }
  ]
}
```

---

### `aibt.unity.is-cooldown-ready` (v1)

- **Summary:** Succeeds once the tree time has reached the time stored in a float key by aibt.unity.start-cooldown.
- **Category:** Checks
- **Kind:** Condition
- **When to use:** Use to gate an ability. A key that was never started (0) is ready.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.is-cooldown-ready",
  "version": 1,
  "summary": "Succeeds once the tree time has reached the time stored in a float key by aibt.unity.start-cooldown.",
  "category": "Checks",
  "kind": "condition",
  "whenToUse": "Use to gate an ability. A key that was never started (0) is ready.",
  "whenNotToUse": "",
  "parameters": {
    "interrupt": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "both",
        "lower-priority",
        "none",
        "self"
      ],
      "default": "none"
    },
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float32"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "not-applicable",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Is cooldown ready node",
      "parameters": {
        "interrupt": "none",
        "key": "key"
      },
      "expectedBehavior": "Succeeds once the tree time has reached the time stored in a float key by aibt.unity.start-cooldown."
    }
  ]
}
```

---

### `aibt.unity.is-true` (v1)

- **Summary:** Succeeds while a bool blackboard value equals the expected value.
- **Category:** Blackboard
- **Kind:** Condition
- **When to use:** Use to gate a branch on a fact a script or another node writes.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.is-true",
  "version": 1,
  "summary": "Succeeds while a bool blackboard value equals the expected value.",
  "category": "Blackboard",
  "kind": "condition",
  "whenToUse": "Use to gate a branch on a fact a script or another node writes.",
  "whenNotToUse": "",
  "parameters": {
    "expected": {
      "type": "boolean",
      "required": false,
      "default": true
    },
    "interrupt": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "both",
        "lower-priority",
        "none",
        "self"
      ],
      "default": "none"
    },
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Bool"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "not-applicable",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Is true node",
      "parameters": {
        "expected": true,
        "interrupt": "none",
        "key": "key"
      },
      "expectedBehavior": "Succeeds while a bool blackboard value equals the expected value."
    }
  ]
}
```

---

### `aibt.unity.is-vector3-set` (v1)

- **Summary:** Succeeds while a Vector3 blackboard value is not (0, 0, 0), the value of a key nothing has written.
- **Category:** Blackboard
- **Kind:** Condition
- **When to use:** Use to check that a position such as 'last-seen' has been filled in.
- **When not to use:** When the world origin is a valid position, track it with a separate bool key instead.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.is-vector3-set",
  "version": 1,
  "summary": "Succeeds while a Vector3 blackboard value is not (0, 0, 0), the value of a key nothing has written.",
  "category": "Blackboard",
  "kind": "condition",
  "whenToUse": "Use to check that a position such as 'last-seen' has been filled in.",
  "whenNotToUse": "When the world origin is a valid position, track it with a separate bool key instead.",
  "parameters": {
    "interrupt": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "both",
        "lower-priority",
        "none",
        "self"
      ],
      "default": "none"
    },
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "not-applicable",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Is vector3 set node",
      "parameters": {
        "interrupt": "none",
        "key": "key"
      },
      "expectedBehavior": "Succeeds while a Vector3 blackboard value is not (0, 0, 0), the value of a key nothing has written."
    }
  ]
}
```

---

### `aibt.unity.is-within-distance` (v1)

- **Summary:** Succeeds while the agent is within a distance of a position.
- **Category:** Movement
- **Kind:** Condition
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.is-within-distance",
  "version": 1,
  "summary": "Succeeds while the agent is within a distance of a position.",
  "category": "Movement",
  "kind": "condition",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "distance": {
      "type": "float32",
      "required": false,
      "default": 5.0
    },
    "interrupt": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "both",
        "lower-priority",
        "none",
        "self"
      ],
      "default": "none"
    },
    "keep-height": {
      "type": "boolean",
      "required": false,
      "default": false
    },
    "target": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "not-applicable",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Is within distance node",
      "parameters": {
        "distance": 5,
        "interrupt": "none",
        "keep-height": false,
        "target": "target"
      },
      "expectedBehavior": "Succeeds while the agent is within a distance of a position."
    }
  ]
}
```

---

### `aibt.unity.keep-distance` (v1)

- **Summary:** Moves the agent towards or away from a position until its distance is between a minimum and a maximum, then succeeds.
- **Category:** Movement
- **Kind:** Action
- **When to use:** Use for ranged attackers and companions that should stay neither too close nor too far. Set min-distance alone to flee.
- **When not to use:** Use a NavMesh node when obstacles are in the way; this moves in a straight line.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.keep-distance",
  "version": 1,
  "summary": "Moves the agent towards or away from a position until its distance is between a minimum and a maximum, then succeeds.",
  "category": "Movement",
  "kind": "action",
  "whenToUse": "Use for ranged attackers and companions that should stay neither too close nor too far. Set min-distance alone to flee.",
  "whenNotToUse": "Use a NavMesh node when obstacles are in the way; this moves in a straight line.",
  "parameters": {
    "face-target": {
      "type": "boolean",
      "required": false,
      "default": true
    },
    "keep-height": {
      "type": "boolean",
      "required": false,
      "default": true
    },
    "max-distance": {
      "type": "float32",
      "required": false,
      "default": 6.0
    },
    "min-distance": {
      "type": "float32",
      "required": false,
      "default": 3.0
    },
    "speed": {
      "type": "float32",
      "required": false,
      "default": 3.0
    },
    "target": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Keep distance node",
      "parameters": {
        "face-target": true,
        "keep-height": true,
        "max-distance": 6,
        "min-distance": 3,
        "speed": 3,
        "target": "target"
      },
      "expectedBehavior": "Moves the agent towards or away from a position until its distance is between a minimum and a maximum, then succeeds."
    }
  ]
}
```

---

### `aibt.unity.line-of-sight` (v1)

- **Summary:** Succeeds while a physics raycast from the agent's eyes reaches a position without hitting other colliders.
- **Category:** Checks
- **Kind:** Condition
- **When to use:** Use for 'can I see the player' checks. Colliders of the agent itself and of target-reference do not block. Add a field of view and range to model vision.
- **When not to use:** Use aibt.unity.is-within-distance when walls do not matter; a raycast costs more.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.line-of-sight",
  "version": 1,
  "summary": "Succeeds while a physics raycast from the agent's eyes reaches a position without hitting other colliders.",
  "category": "Checks",
  "kind": "condition",
  "whenToUse": "Use for 'can I see the player' checks. Colliders of the agent itself and of target-reference do not block. Add a field of view and range to model vision.",
  "whenNotToUse": "Use aibt.unity.is-within-distance when walls do not matter; a raycast costs more.",
  "parameters": {
    "eye-height": {
      "type": "float32",
      "required": false,
      "default": 1.5
    },
    "field-of-view": {
      "type": "float32",
      "required": false,
      "default": 360.0
    },
    "interrupt": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "both",
        "lower-priority",
        "none",
        "self"
      ],
      "default": "none"
    },
    "layer-mask": {
      "type": "int32",
      "required": false,
      "default": -1
    },
    "max-distance": {
      "type": "float32",
      "required": false,
      "default": 20.0
    },
    "target": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    },
    "target-height": {
      "type": "float32",
      "required": false,
      "default": 1.0
    },
    "target-reference": {
      "type": "string",
      "required": false,
      "default": ""
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "not-applicable",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Line of sight node",
      "parameters": {
        "eye-height": 1.5,
        "field-of-view": 360,
        "interrupt": "none",
        "layer-mask": -1,
        "max-distance": 20,
        "target": "target",
        "target-height": 1,
        "target-reference": ""
      },
      "expectedBehavior": "Succeeds while a physics raycast from the agent's eyes reaches a position without hitting other colliders."
    }
  ]
}
```

---

### `aibt.unity.log` (v1)

- **Summary:** Writes a message to the Unity Console and succeeds.
- **Category:** Debug
- **Kind:** Action
- **When to use:** Use to see when a branch runs while building a tree.
- **When not to use:** Remove from finished trees that tick every frame; logging is slow.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.log",
  "version": 1,
  "summary": "Writes a message to the Unity Console and succeeds.",
  "category": "Debug",
  "kind": "action",
  "whenToUse": "Use to see when a branch runs while building a tree.",
  "whenNotToUse": "Remove from finished trees that tick every frame; logging is slow.",
  "parameters": {
    "level": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "error",
        "info",
        "warning"
      ],
      "default": "info"
    },
    "message": {
      "type": "string",
      "required": false,
      "default": "Behavior tree log"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Log node",
      "parameters": {
        "level": "info",
        "message": "Behavior tree log"
      },
      "expectedBehavior": "Writes a message to the Unity Console and succeeds."
    }
  ]
}
```

---

### `aibt.unity.look-at` (v1)

- **Summary:** Turns the agent towards a position and succeeds once it faces it.
- **Category:** Movement
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.look-at",
  "version": 1,
  "summary": "Turns the agent towards a position and succeeds once it faces it.",
  "category": "Movement",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "degrees-per-second": {
      "type": "float32",
      "required": false,
      "default": 360.0
    },
    "keep-height": {
      "type": "boolean",
      "required": false,
      "default": true
    },
    "target": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Look at node",
      "parameters": {
        "degrees-per-second": 360,
        "keep-height": true,
        "target": "target"
      },
      "expectedBehavior": "Turns the agent towards a position and succeeds once it faces it."
    }
  ]
}
```

---

### `aibt.unity.move-direction` (v1)

- **Summary:** Moves the agent along a direction at a speed for a duration, then succeeds.
- **Category:** Movement
- **Kind:** Action
- **When to use:** Use for dashes, knock-back or drifting. Without a direction key it moves along its own forward. A duration of 0 keeps moving until a parent interrupts it.
- **When not to use:** Use aibt.unity.move-towards to reach a position.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.move-direction",
  "version": 1,
  "summary": "Moves the agent along a direction at a speed for a duration, then succeeds.",
  "category": "Movement",
  "kind": "action",
  "whenToUse": "Use for dashes, knock-back or drifting. Without a direction key it moves along its own forward. A duration of 0 keeps moving until a parent interrupts it.",
  "whenNotToUse": "Use aibt.unity.move-towards to reach a position.",
  "parameters": {
    "direction": {
      "type": "blackboard-key",
      "required": false,
      "valueType": "Float3"
    },
    "duration": {
      "type": "float32",
      "required": false,
      "default": 1.0
    },
    "face-direction": {
      "type": "boolean",
      "required": false,
      "default": false
    },
    "speed": {
      "type": "float32",
      "required": false,
      "default": 2.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Move direction node",
      "parameters": {
        "direction": "direction",
        "duration": 1,
        "face-direction": false,
        "speed": 2
      },
      "expectedBehavior": "Moves the agent along a direction at a speed for a duration, then succeeds."
    }
  ]
}
```

---

### `aibt.unity.move-towards` (v1)

- **Summary:** Moves the agent's Transform in a straight line to a position and succeeds on arrival.
- **Category:** Movement
- **Kind:** Action
- **When to use:** Use for simple movement without obstacles. The target is re-read every tick, so it can move.
- **When not to use:** Use a NavMesh or your own locomotion node when the agent must path around obstacles.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.move-towards",
  "version": 1,
  "summary": "Moves the agent's Transform in a straight line to a position and succeeds on arrival.",
  "category": "Movement",
  "kind": "action",
  "whenToUse": "Use for simple movement without obstacles. The target is re-read every tick, so it can move.",
  "whenNotToUse": "Use a NavMesh or your own locomotion node when the agent must path around obstacles.",
  "parameters": {
    "face-direction": {
      "type": "boolean",
      "required": false,
      "default": true
    },
    "keep-height": {
      "type": "boolean",
      "required": false,
      "default": false
    },
    "speed": {
      "type": "float32",
      "required": false,
      "default": 3.0
    },
    "stop-distance": {
      "type": "float32",
      "required": false,
      "default": 0.1
    },
    "target": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Move towards node",
      "parameters": {
        "face-direction": true,
        "keep-height": false,
        "speed": 3,
        "stop-distance": 0.1,
        "target": "target"
      },
      "expectedBehavior": "Moves the agent's Transform in a straight line to a position and succeeds on arrival."
    }
  ]
}
```

---

### `aibt.unity.nav-move-to` (v1)

- **Summary:** Sends the agent's NavMeshAgent to a position and succeeds on arrival; fails when no complete path exists.
- **Category:** Navigation
- **Kind:** Action
- **When to use:** Use for movement around obstacles on a baked NavMesh. The target is re-read every tick, so it can move.
- **When not to use:** Use aibt.unity.move-towards for an agent without a NavMeshAgent.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.nav-move-to",
  "version": 1,
  "summary": "Sends the agent's NavMeshAgent to a position and succeeds on arrival; fails when no complete path exists.",
  "category": "Navigation",
  "kind": "action",
  "whenToUse": "Use for movement around obstacles on a baked NavMesh. The target is re-read every tick, so it can move.",
  "whenNotToUse": "Use aibt.unity.move-towards for an agent without a NavMeshAgent.",
  "parameters": {
    "repath-distance": {
      "type": "float32",
      "required": false,
      "default": 0.5
    },
    "speed": {
      "type": "float32",
      "required": false,
      "default": 0.0
    },
    "stop-distance": {
      "type": "float32",
      "required": false,
      "default": 0.5
    },
    "target": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Nav move to node",
      "parameters": {
        "repath-distance": 0.5,
        "speed": 0,
        "stop-distance": 0.5,
        "target": "target"
      },
      "expectedBehavior": "Sends the agent's NavMeshAgent to a position and succeeds on arrival; fails when no complete path exists."
    }
  ]
}
```

---

### `aibt.unity.nav-stop` (v1)

- **Summary:** Stops the agent's NavMeshAgent and clears its path, then succeeds.
- **Category:** Navigation
- **Kind:** Action
- **When to use:** Use before an attack or animation that must not slide the agent along an old path.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.nav-stop",
  "version": 1,
  "summary": "Stops the agent's NavMeshAgent and clears its path, then succeeds.",
  "category": "Navigation",
  "kind": "action",
  "whenToUse": "Use before an attack or animation that must not slide the agent along an old path.",
  "whenNotToUse": "",
  "parameters": {},
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Nav stop node",
      "parameters": {},
      "expectedBehavior": "Stops the agent's NavMeshAgent and clears its path, then succeeds."
    }
  ]
}
```

---

### `aibt.unity.play-audio` (v1)

- **Summary:** Plays an AudioSource. Succeeds at once, or when the clip ends if wait-until-finished is set; fails when there is no AudioSource.
- **Category:** Audio
- **Kind:** Action
- **When to use:** Use for barks and effect sounds. With wait-until-finished an interrupt stops the sound.
- **When not to use:** For overlapping one-shots on one source, call AudioSource.PlayOneShot from your own node.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.play-audio",
  "version": 1,
  "summary": "Plays an AudioSource. Succeeds at once, or when the clip ends if wait-until-finished is set; fails when there is no AudioSource.",
  "category": "Audio",
  "kind": "action",
  "whenToUse": "Use for barks and effect sounds. With wait-until-finished an interrupt stops the sound.",
  "whenNotToUse": "For overlapping one-shots on one source, call AudioSource.PlayOneShot from your own node.",
  "parameters": {
    "reference": {
      "type": "string",
      "required": false,
      "default": ""
    },
    "wait-until-finished": {
      "type": "boolean",
      "required": false,
      "default": false
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Play audio node",
      "parameters": {
        "reference": "",
        "wait-until-finished": false
      },
      "expectedBehavior": "Plays an AudioSource. Succeeds at once, or when the clip ends if wait-until-finished is set; fails when there is no AudioSource."
    }
  ]
}
```

---

### `aibt.unity.raise-event` (v1)

- **Summary:** Raises a named event on the agent (agent.Events and the inspector's Event Responses) and succeeds.
- **Category:** Events
- **Kind:** Action
- **When to use:** Use to tell gameplay code something happened, such as 'spotted-player', without writing a node for it.
- **When not to use:** Use a blackboard key when gameplay code should read a lasting state rather than react to a moment.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.raise-event",
  "version": 1,
  "summary": "Raises a named event on the agent (agent.Events and the inspector's Event Responses) and succeeds.",
  "category": "Events",
  "kind": "action",
  "whenToUse": "Use to tell gameplay code something happened, such as 'spotted-player', without writing a node for it.",
  "whenNotToUse": "Use a blackboard key when gameplay code should read a lasting state rather than react to a moment.",
  "parameters": {
    "event-name": {
      "type": "string",
      "required": false,
      "default": "event"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Raise event node",
      "parameters": {
        "event-name": "event"
      },
      "expectedBehavior": "Raises a named event on the agent (agent.Events and the inspector's Event Responses) and succeeds."
    }
  ]
}
```

---

### `aibt.unity.random-chance` (v1)

- **Summary:** Succeeds with a probability from 0 to 1 each time it is checked.
- **Category:** Checks
- **Kind:** Condition
- **When to use:** Use to vary behavior, for example a 30% chance to taunt before attacking.
- **When not to use:** Do not give it an interrupt mode: it would re-roll every tick.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.random-chance",
  "version": 1,
  "summary": "Succeeds with a probability from 0 to 1 each time it is checked.",
  "category": "Checks",
  "kind": "condition",
  "whenToUse": "Use to vary behavior, for example a 30% chance to taunt before attacking.",
  "whenNotToUse": "Do not give it an interrupt mode: it would re-roll every tick.",
  "parameters": {
    "chance": {
      "type": "float32",
      "required": false,
      "default": 0.5
    },
    "interrupt": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "both",
        "lower-priority",
        "none",
        "self"
      ],
      "default": "none"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "not-applicable",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Random chance node",
      "parameters": {
        "chance": 0.5,
        "interrupt": "none"
      },
      "expectedBehavior": "Succeeds with a probability from 0 to 1 each time it is checked."
    }
  ]
}
```

---

### `aibt.unity.random-float` (v1)

- **Summary:** Writes a random float between min and max to a blackboard key and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.random-float",
  "version": 1,
  "summary": "Writes a random float between min and max to a blackboard key and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float32"
    },
    "max": {
      "type": "float32",
      "required": false,
      "default": 1.0
    },
    "min": {
      "type": "float32",
      "required": false,
      "default": 0.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Random float node",
      "parameters": {
        "key": "key",
        "max": 1,
        "min": 0
      },
      "expectedBehavior": "Writes a random float between min and max to a blackboard key and succeeds."
    }
  ]
}
```

---

### `aibt.unity.random-int` (v1)

- **Summary:** Writes a random int from min to max, both included, to a blackboard key and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.random-int",
  "version": 1,
  "summary": "Writes a random int from min to max, both included, to a blackboard key and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Int32"
    },
    "max": {
      "type": "int32",
      "required": false,
      "default": 10
    },
    "min": {
      "type": "int32",
      "required": false,
      "default": 0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Random int node",
      "parameters": {
        "key": "key",
        "max": 10,
        "min": 0
      },
      "expectedBehavior": "Writes a random int from min to max, both included, to a blackboard key and succeeds."
    }
  ]
}
```

---

### `aibt.unity.random-point` (v1)

- **Summary:** Writes a random point within a radius of a center to a Vector3 key and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **When to use:** Use for wandering: pick a point, then move to it. Without a center key the agent's position when the tree started is used, so it stays near home.
- **When not to use:** On a NavMesh, a random point can be unreachable; use aibt.unity.nav-move-to, which fails on such points, or your own sampler.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.random-point",
  "version": 1,
  "summary": "Writes a random point within a radius of a center to a Vector3 key and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "Use for wandering: pick a point, then move to it. Without a center key the agent's position when the tree started is used, so it stays near home.",
  "whenNotToUse": "On a NavMesh, a random point can be unreachable; use aibt.unity.nav-move-to, which fails on such points, or your own sampler.",
  "parameters": {
    "center": {
      "type": "blackboard-key",
      "required": false,
      "valueType": "Float3"
    },
    "flat": {
      "type": "boolean",
      "required": false,
      "default": true
    },
    "min-radius": {
      "type": "float32",
      "required": false,
      "default": 0.0
    },
    "radius": {
      "type": "float32",
      "required": false,
      "default": 5.0
    },
    "result": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Random point node",
      "parameters": {
        "center": "center",
        "flat": true,
        "min-radius": 0,
        "radius": 5,
        "result": "result"
      },
      "expectedBehavior": "Writes a random point within a radius of a center to a Vector3 key and succeeds."
    }
  ]
}
```

---

### `aibt.unity.rotate-towards` (v1)

- **Summary:** Turns the agent to face along a direction stored in a Vector3 key and succeeds once it does.
- **Category:** Movement
- **Kind:** Action
- **When to use:** Use when you have a direction (a velocity, a normal, a 'facing' key) rather than a position.
- **When not to use:** Use aibt.unity.look-at to face a position.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.rotate-towards",
  "version": 1,
  "summary": "Turns the agent to face along a direction stored in a Vector3 key and succeeds once it does.",
  "category": "Movement",
  "kind": "action",
  "whenToUse": "Use when you have a direction (a velocity, a normal, a 'facing' key) rather than a position.",
  "whenNotToUse": "Use aibt.unity.look-at to face a position.",
  "parameters": {
    "degrees-per-second": {
      "type": "float32",
      "required": false,
      "default": 360.0
    },
    "direction": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    },
    "keep-upright": {
      "type": "boolean",
      "required": false,
      "default": true
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Rotate towards node",
      "parameters": {
        "degrees-per-second": 360,
        "direction": "direction",
        "keep-upright": true
      },
      "expectedBehavior": "Turns the agent to face along a direction stored in a Vector3 key and succeeds once it does."
    }
  ]
}
```

---

### `aibt.unity.set-active` (v1)

- **Summary:** Activates or deactivates a GameObject and succeeds; fails when the reference is missing.
- **Category:** GameObject
- **Kind:** Action
- **When to use:** Use to show a weapon, an alert icon or an effect. Deactivating the agent itself also stops its tree.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.set-active",
  "version": 1,
  "summary": "Activates or deactivates a GameObject and succeeds; fails when the reference is missing.",
  "category": "GameObject",
  "kind": "action",
  "whenToUse": "Use to show a weapon, an alert icon or an effect. Deactivating the agent itself also stops its tree.",
  "whenNotToUse": "",
  "parameters": {
    "active": {
      "type": "boolean",
      "required": false,
      "default": true
    },
    "reference": {
      "type": "string",
      "required": false,
      "default": ""
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Set active node",
      "parameters": {
        "active": true,
        "reference": ""
      },
      "expectedBehavior": "Activates or deactivates a GameObject and succeeds; fails when the reference is missing."
    }
  ]
}
```

---

### `aibt.unity.set-bool` (v1)

- **Summary:** Writes a constant to a bool blackboard key and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.set-bool",
  "version": 1,
  "summary": "Writes a constant to a bool blackboard key and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Bool"
    },
    "value": {
      "type": "boolean",
      "required": false,
      "default": true
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Set bool node",
      "parameters": {
        "key": "key",
        "value": true
      },
      "expectedBehavior": "Writes a constant to a bool blackboard key and succeeds."
    }
  ]
}
```

---

### `aibt.unity.set-float` (v1)

- **Summary:** Writes a constant to a float blackboard key and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.set-float",
  "version": 1,
  "summary": "Writes a constant to a float blackboard key and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float32"
    },
    "value": {
      "type": "float32",
      "required": false,
      "default": 0.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Set float node",
      "parameters": {
        "key": "key",
        "value": 0
      },
      "expectedBehavior": "Writes a constant to a float blackboard key and succeeds."
    }
  ]
}
```

---

### `aibt.unity.set-int` (v1)

- **Summary:** Writes a constant to an int blackboard key and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.set-int",
  "version": 1,
  "summary": "Writes a constant to an int blackboard key and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Int32"
    },
    "value": {
      "type": "int32",
      "required": false,
      "default": 0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Set int node",
      "parameters": {
        "key": "key",
        "value": 0
      },
      "expectedBehavior": "Writes a constant to an int blackboard key and succeeds."
    }
  ]
}
```

---

### `aibt.unity.set-vector3` (v1)

- **Summary:** Writes a constant Vector3 to a blackboard key and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.set-vector3",
  "version": 1,
  "summary": "Writes a constant Vector3 to a blackboard key and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float3"
    },
    "x": {
      "type": "float32",
      "required": false,
      "default": 0.0
    },
    "y": {
      "type": "float32",
      "required": false,
      "default": 0.0
    },
    "z": {
      "type": "float32",
      "required": false,
      "default": 0.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Set vector3 node",
      "parameters": {
        "key": "key",
        "x": 0,
        "y": 0,
        "z": 0
      },
      "expectedBehavior": "Writes a constant Vector3 to a blackboard key and succeeds."
    }
  ]
}
```

---

### `aibt.unity.start-cooldown` (v1)

- **Summary:** Writes the tree time at which a cooldown ends to a float key and succeeds. Pair it with aibt.unity.is-cooldown-ready.
- **Category:** Checks
- **Kind:** Action
- **When to use:** Use after an ability fires. Several branches, and gameplay code, can share one cooldown through its key.
- **When not to use:** Use the aibt.core.cooldown decorator for a cooldown private to one branch.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.start-cooldown",
  "version": 1,
  "summary": "Writes the tree time at which a cooldown ends to a float key and succeeds. Pair it with aibt.unity.is-cooldown-ready.",
  "category": "Checks",
  "kind": "action",
  "whenToUse": "Use after an ability fires. Several branches, and gameplay code, can share one cooldown through its key.",
  "whenNotToUse": "Use the aibt.core.cooldown decorator for a cooldown private to one branch.",
  "parameters": {
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Float32"
    },
    "seconds": {
      "type": "float32",
      "required": false,
      "default": 2.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Start cooldown node",
      "parameters": {
        "key": "key",
        "seconds": 2
      },
      "expectedBehavior": "Writes the tree time at which a cooldown ends to a float key and succeeds. Pair it with aibt.unity.is-cooldown-ready."
    }
  ]
}
```

---

### `aibt.unity.succeed` (v1)

- **Summary:** Succeeds immediately.
- **Category:** Flow
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.succeed",
  "version": 1,
  "summary": "Succeeds immediately.",
  "category": "Flow",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {},
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Succeed node",
      "parameters": {},
      "expectedBehavior": "Succeeds immediately."
    }
  ]
}
```

---

### `aibt.unity.time-since-changed` (v1)

- **Summary:** Compares the seconds since a blackboard key last changed value with a constant.
- **Category:** Checks
- **Kind:** Condition
- **When to use:** Use for 'lost track of the target for 5 seconds' or 'alarm raised less than 2 seconds ago'. Any key type works.
- **When not to use:** A write of the same value is not a change; use a counter key when every write must count.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** NotApplicable
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.time-since-changed",
  "version": 1,
  "summary": "Compares the seconds since a blackboard key last changed value with a constant.",
  "category": "Checks",
  "kind": "condition",
  "whenToUse": "Use for 'lost track of the target for 5 seconds' or 'alarm raised less than 2 seconds ago'. Any key type works.",
  "whenNotToUse": "A write of the same value is not a change; use a counter key when every write must count.",
  "parameters": {
    "comparison": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "equal",
        "greater-or-equal",
        "greater-than",
        "less-or-equal",
        "less-than",
        "not-equal"
      ],
      "default": "greater-or-equal"
    },
    "interrupt": {
      "type": "string-enum",
      "required": false,
      "allowedValues": [
        "both",
        "lower-priority",
        "none",
        "self"
      ],
      "default": "none"
    },
    "key": {
      "type": "blackboard-key",
      "required": true
    },
    "seconds": {
      "type": "float32",
      "required": false,
      "default": 5.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "not-applicable",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Time since changed node",
      "parameters": {
        "comparison": "greater-or-equal",
        "interrupt": "none",
        "key": "key",
        "seconds": 5
      },
      "expectedBehavior": "Compares the seconds since a blackboard key last changed value with a constant."
    }
  ]
}
```

---

### `aibt.unity.toggle-bool` (v1)

- **Summary:** Inverts a bool blackboard value and succeeds.
- **Category:** Blackboard
- **Kind:** Action
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.toggle-bool",
  "version": 1,
  "summary": "Inverts a bool blackboard value and succeeds.",
  "category": "Blackboard",
  "kind": "action",
  "whenToUse": "",
  "whenNotToUse": "",
  "parameters": {
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Bool"
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Toggle bool node",
      "parameters": {
        "key": "key"
      },
      "expectedBehavior": "Inverts a bool blackboard value and succeeds."
    }
  ]
}
```

---

### `aibt.unity.turn-by` (v1)

- **Summary:** Turns the agent around its up axis by an angle at a speed and succeeds when the turn is done.
- **Category:** Movement
- **Kind:** Action
- **When to use:** Use to look around while idle or searching. A negative angle turns left.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.turn-by",
  "version": 1,
  "summary": "Turns the agent around its up axis by an angle at a speed and succeeds when the turn is done.",
  "category": "Movement",
  "kind": "action",
  "whenToUse": "Use to look around while idle or searching. A negative angle turns left.",
  "whenNotToUse": "",
  "parameters": {
    "degrees": {
      "type": "float32",
      "required": false,
      "default": 90.0
    },
    "degrees-per-second": {
      "type": "float32",
      "required": false,
      "default": 180.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Turn by node",
      "parameters": {
        "degrees": 90,
        "degrees-per-second": 180
      },
      "expectedBehavior": "Turns the agent around its up axis by an angle at a speed and succeeds when the turn is done."
    }
  ]
}
```

---

### `aibt.unity.wait-frames` (v1)

- **Summary:** Stays running for a number of tree ticks, then succeeds.
- **Category:** Flow
- **Kind:** Action
- **When to use:** Use to let physics or another script settle for a frame or two. A tick is a frame when Tick Mode is Every Frame.
- **When not to use:** Use aibt.unity.wait-seconds for game-time pauses; frame counts change with the frame rate.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.wait-frames",
  "version": 1,
  "summary": "Stays running for a number of tree ticks, then succeeds.",
  "category": "Flow",
  "kind": "action",
  "whenToUse": "Use to let physics or another script settle for a frame or two. A tick is a frame when Tick Mode is Every Frame.",
  "whenNotToUse": "Use aibt.unity.wait-seconds for game-time pauses; frame counts change with the frame rate.",
  "parameters": {
    "frames": {
      "type": "int32",
      "required": false,
      "default": 1
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Wait frames node",
      "parameters": {
        "frames": 1
      },
      "expectedBehavior": "Stays running for a number of tree ticks, then succeeds."
    }
  ]
}
```

---

### `aibt.unity.wait-random` (v1)

- **Summary:** Stays running for a random duration between two values in seconds, then succeeds.
- **Category:** Flow
- **Kind:** Action
- **When to use:** Use for pauses that should not look mechanical, such as idling between wander points.
- **When not to use:** Use aibt.unity.wait-seconds when the duration must be exact.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.wait-random",
  "version": 1,
  "summary": "Stays running for a random duration between two values in seconds, then succeeds.",
  "category": "Flow",
  "kind": "action",
  "whenToUse": "Use for pauses that should not look mechanical, such as idling between wander points.",
  "whenNotToUse": "Use aibt.unity.wait-seconds when the duration must be exact.",
  "parameters": {
    "max-seconds": {
      "type": "float32",
      "required": false,
      "default": 3.0
    },
    "min-seconds": {
      "type": "float32",
      "required": false,
      "default": 1.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Wait random node",
      "parameters": {
        "max-seconds": 3,
        "min-seconds": 1
      },
      "expectedBehavior": "Stays running for a random duration between two values in seconds, then succeeds."
    }
  ]
}
```

---

### `aibt.unity.wait-seconds` (v1)

- **Summary:** Stays running for a duration in seconds, then succeeds.
- **Category:** Flow
- **Kind:** Action
- **When to use:** Use for pauses measured in game time.
- **When not to use:** Use aibt.core.cooldown to rate-limit a branch instead of delaying it.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.wait-seconds",
  "version": 1,
  "summary": "Stays running for a duration in seconds, then succeeds.",
  "category": "Flow",
  "kind": "action",
  "whenToUse": "Use for pauses measured in game time.",
  "whenNotToUse": "Use aibt.core.cooldown to rate-limit a branch instead of delaying it.",
  "parameters": {
    "random-extra": {
      "type": "float32",
      "required": false,
      "default": 0.0
    },
    "seconds": {
      "type": "float32",
      "required": false,
      "default": 1.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Wait seconds node",
      "parameters": {
        "random-extra": 0,
        "seconds": 1
      },
      "expectedBehavior": "Stays running for a duration in seconds, then succeeds."
    }
  ]
}
```

---

### `aibt.unity.wait-until` (v1)

- **Summary:** Stays running until a bool blackboard value equals the expected value, then succeeds.
- **Category:** Flow
- **Kind:** Action
- **When to use:** Use to pause a sequence until gameplay code or another branch sets a flag, such as 'door-open'.
- **When not to use:** Use aibt.unity.is-true with an interrupt when the flag should cancel running work instead of resuming it.
- **Execution domain:** MainThread
- **Deterministic:** False
- **Cancellation:** AbortOnly
- **Cost hint:** Variable

Full contract (verbatim `get_node_contract` output):

```json
{
  "typeId": "aibt.unity.wait-until",
  "version": 1,
  "summary": "Stays running until a bool blackboard value equals the expected value, then succeeds.",
  "category": "Flow",
  "kind": "action",
  "whenToUse": "Use to pause a sequence until gameplay code or another branch sets a flag, such as 'door-open'.",
  "whenNotToUse": "Use aibt.unity.is-true with an interrupt when the flag should cancel running work instead of resuming it.",
  "parameters": {
    "expected": {
      "type": "boolean",
      "required": false,
      "default": true
    },
    "key": {
      "type": "blackboard-key",
      "required": true,
      "valueType": "Bool"
    },
    "timeout-seconds": {
      "type": "float32",
      "required": false,
      "default": 0.0
    }
  },
  "childPolicy": {
    "minimum": 0,
    "maximum": 0,
    "ordered": true
  },
  "reads": [],
  "writes": [],
  "sideEffects": [],
  "possibleStatuses": [
    "failure",
    "running",
    "success"
  ],
  "memory": {
    "size": 0,
    "alignment": 1,
    "lifetime": "activation"
  },
  "configuration": {
    "size": 0,
    "alignment": 1
  },
  "cancellation": "abort-only",
  "executionDomain": "main-thread",
  "deterministic": false,
  "costHint": "variable",
  "examples": [
    {
      "title": "Wait until node",
      "parameters": {
        "expected": true,
        "key": "key",
        "timeout-seconds": 0
      },
      "expectedBehavior": "Stays running until a bool blackboard value equals the expected value, then succeeds."
    }
  ]
}
```
