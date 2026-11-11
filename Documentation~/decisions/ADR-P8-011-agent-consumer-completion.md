# AIBT-044 — Consistent agent contracts and explicit execution backends

Status: Accepted by owner's approved proposal, 2026-11-06.

Project node contracts are shared between graph, discovery, authoring and validation. Logical
authoring validation does not require a reference implementation of a generated native node.
Reference compile/simulate remain explicit and must not silently execute native-only gameplay.
Native preparation names its generated catalog and reuses the existing Editor baker.

New additive shared operations expose operation schemas, runtime catalogs, native preparation,
read-only host/trace snapshots and explicit expected-outcome native node checks. Existing tools
remain compatible. Compilation permission covers preparation of a derived runtime asset (not a
semantic source edit); source hash, dry-run and explicit overwrite constrain its effects. Read
permission covers schemas/docs/trace. No transport grants itself extra operation permissions.

Unity object/asset/trace operations execute on the Editor thread. Trace reads use existing
non-blocking snapshot APIs; busy writers return retryable/unavailable state, never Complete().
Output identities include source/program/instance as available; dropped history is disclosed.

Documentation resources resolve the actual package root. Generated project documentation is
written to an explicit project-owned destination, not an installed package cache. Checked-in
package documentation remains deterministic and distinguishes built-ins from project contracts.

Behavior expectations come from caller-authored inputs and assertions, not from observing a
first run. A smoke dispatch result remains distinct from a behavior test. No runtime semantics,
native ABI or canonical tree format changes are authorized by this task.

Applied Burst node assemblies target runtime as well as Editor; staging stays Editor-only and
quarantined. Generated case data is guarded by UNITY_EDITOR and consumed by test_node, not a
pretend NUnit test. This removes the prior Inconclusive scaffold without adding NUnit/runtime
dependencies. Template condition cases derive from its declared threshold contract; custom
expectations can replace them before compilation and are included in the staged source hash.
