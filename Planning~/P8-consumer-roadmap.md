# AIBT: human authoring and production-readiness roadmap

This is an implementation and acceptance checklist, not a 1.0 readiness claim. P8 work items remain
in progress until their own checks are complete. Existing architecture and versioned contracts
remain authoritative.

## Comparison baseline

- [Unity Behavior](https://docs.unity3d.com/Packages/com.unity.behavior@1.0/manual/index.html)
  combines readable graphs, reusable subgraphs, C# integration, built-in gameplay nodes, and
  live Play-mode visualization. These are useful consumer-workflow reference points; AIBT's
  generated native execution and explicit Burst boundaries have different goals.
- [BehaviorTree.CPP / Groot2](https://www.behaviortree.dev/groot/) combines graphical authoring,
  runtime monitoring and recorded execution. Some monitoring capabilities are commercial.
- [py_trees](https://py-trees.readthedocs.io/en/devel/blackboards.html) documents blackboard
  access and observation explicitly. Its execution and language model are different from AIBT's.

These comparisons identify usability gaps; they do not imply equal performance or feature parity.

## Current work and remaining acceptance

1. **Graph authoring (P8-001, P8-004, P8-006, P8-007).** Create, edit and save drafts;
   discover project nodes; edit supported Tree-scope variables and generated blackboard bindings;
   run the existing generated-program compiler against the draft. Automated checks cover these
   paths. Manually validate the entire workflow on the supported Unity baseline, including focus,
   selection, dialogs, undo/redo, domain reload and external-file changes. Port drag-to-connect,
   source layout and interaction quality remain to be assessed in a live editor.
2. **Reference execution (P8-005).** Built-in and project managed leaves have independent state
   per node; preview policy is explicit. Verify lifecycle and error presentation in a manual
   end-to-end workflow. Reference preview is not a substitute for native execution.
3. **Live debugging (P8-008).** Attach a selected bootstrapped host to the existing timeline.
   A verified mapping from that exact live program to its source graph is still needed for safe
   graph highlighting; the generic attachment does not guess. Verify real Play-mode behavior,
   trace loss, agent destruction, reattachment and lifecycle handling.
4. **Production integration.** The generated compiler, dispatch catalog, `ProductionTreeHost`,
   `ProductionTreeScheduler` and `ProductionTreeWorld` exist, with a Swarm Arena example. A human
   author still needs a straightforward, validated route from an authored asset to a configured
   host in their own game. Establish that path using real consumption and Player acceptance;
   distinguish compile success from execution, effects and gameplay correctness.
5. **Remaining blackboard and format work.** Agent/shared contracts, registered types, and an
   explicit, semantics-preserving v1-to-v2 migration are not exposed by the graph editor. Assess
   these against actual authored trees and existing versioning rules; do not silently migrate or
   drop bindings. Validate defaults and incompatible/missing bindings in the real editor.
6. **Release gates.** Repeat automated tests on Unity 6000.5.8f1, run documented platform and
   Player checks appropriate to changed production code, exercise the samples, and complete a
   fresh-user walkthrough without AI. Keep platform/performance claims within measured evidence.
   No 1.0 or general production-readiness claim follows merely from the beta Editor test pass.
