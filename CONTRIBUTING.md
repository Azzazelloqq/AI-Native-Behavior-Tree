# Contributing

AI-Native Behavior Tree is in its architecture-first stage. Discuss material API, runtime, data-format, editor, scheduler, and MCP changes before implementation.

## Workflow

1. Describe the expected user-visible behavior and constraints.
2. Confirm the architectural direction.
3. Write a concrete implementation and verification plan.
4. Implement the smallest coherent vertical slice.
5. Add behavior tests and, where relevant, performance benchmarks.
6. Update documentation, schemas, examples, and the changelog.

## Pull requests

A pull request should be focused, reviewable, and include:

- the problem and intended behavior;
- architectural impact;
- tests performed;
- benchmark results for performance-sensitive changes;
- compatibility or migration notes;
- documentation changes.

Do not combine formatting sweeps, unrelated refactors, and behavior changes.

## Compatibility

The initial development baseline is Unity 6 (`6000.0` or newer). Additional Unity versions and platforms become supported only after they are exercised by the compatibility and benchmark matrix.

Public contracts are unstable before `1.0.0`, but changes must still be versioned and accompanied by migrations where persisted files are affected.

## Commit style

Use imperative, scoped messages such as `runtime: add sequence execution` or `editor: preserve pinned node positions`. Keep generated outputs out of commits unless they are intentional distributable artifacts.

