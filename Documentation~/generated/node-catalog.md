# AIBT node catalog (generated)

Source: the real AIBT node registry, via `AIBT.Authoring.NodeCatalogQuery` (P6-003). Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command. Do not hand-edit -- edits are overwritten on the next regeneration.

11 registered node type(s).

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
