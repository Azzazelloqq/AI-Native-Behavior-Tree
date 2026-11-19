# AIBT MCP agent workflow guide (generated)

Reflects the actual registered MCP tools (see `AIBT.Mcp.McpBuiltInTools`), not an idealized set. Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command.

## 1. Connect

Use CLI as the command entry point: see [CLI and MCP setup](../cli.md). Open the selected Unity project, then run `aibt --project <UnityProject> mcp ensure`; commands start/recover its Editor bridge on demand. MCP clients can launch the built CLI with `mcp serve`, or retain direct server configuration with `AIBT_PROJECT_PATH` and explicit `AIBT_MCP_PERMISSIONS`. The client and Editor must be on the same machine. No manual Start is needed for this path; closed or unresponsive Unity produces a bounded error.

## 2. Discover

Call `aibt_get_project_manifest` for capabilities, project policy, and the tree/revision listing; `aibt_search_nodes` to search the node catalog by keyword; `aibt_get_node_contract` for one node type's full contract. Also see the generated node catalog document for every node's contract at once.

## 3. Author

Create a tree with `aibt_create_tree`, then edit it with `aibt_add_node`, `aibt_remove_node`, `aibt_move_node`, `aibt_replace_node`, `aibt_configure_node`, or `aibt_set_blackboard_keys`. Use `aibt_extract_subtree`/`aibt_inline_subtree` to move a subtree between trees, `aibt_apply_domain_patch` to apply several operations as one atomic transaction, and `aibt_request_layout` afterward to lay out the affected region. Every mutating call takes the target's current `expectedHash`/`contentHash` -- always use the value the last accepted call returned, never assume a fixed increment (`ADR-P6-002`).

Subtrees (one tree used as a node of another, `ADR-P11-001`): `aibt_add_node` with a node `{id, subtree: {treeId, interfaceVersion?, keys?}}` adds a reference; `aibt_set_subtree_keys` connects its interface keys to parent keys or constants; `aibt_extract_to_subtree` moves a branch into a new tree file and leaves a reference; `aibt_unpack_subtree` replaces a reference by a plain copy; `aibt_find_subtree_usages` lists the trees that use a tree ID; `aibt_inspect_tree` with `expanded=true` shows the expanded tree the compiler sees. These are not `aibt_extract_subtree`/`aibt_inline_subtree`, which move node payloads and create no reference. An edit of a tree with references is accepted only if the tree still expands and compiles; see `Documentation~/subtrees.md`.

## 4. Verify

Run `aibt_validate` and `aibt_compile` against a tree, `aibt_simulate` to step it through the Phase 1 reference executor, `aibt_run_tests` against a `.aibtcase.json` behavior case, and `aibt_run_benchmark` against a real P4-001 scheduling scenario. Use `aibt_explain_diagnostic` to look up a returned diagnostic code's stable meaning.

## 5. Add a custom node

The generate-compile-apply gate: `aibt_generate_node` (stages a new node from a template), `aibt_preview_node_diff` (inspect the staged source before compiling), `aibt_generate_node_tests_and_manifest` (stages expected-outcome cases consumed by native test-node), `aibt_analyze_and_compile_node` (two-call, non-blocking compile check -- call with `mode='start'`, then poll with `mode='check'`), `aibt_test_node` (reports registry/dispatch proof separately from expected-outcome behaviorProven), and finally `aibt_apply_node` (the only step that persists into the real project). Before `apply_node`, changes stay in the quarantined staging directory.

## 6. Custom project tools

Call `aibt_list_custom_tools` to discover any project-registered custom tools, and `aibt_call_custom_tool` to invoke one by name.

## 7. Native consumer workflow

Use `aibt_list_operations` and `aibt_get_operation_schema` for bridge argument schemas and permissions. `aibt_inspect_tree` returns the source and current contentHash; `aibt_runtime_catalogs` lists generated catalogs. `aibt_prepare_runtime` prepares a native asset under Assets with source-hash, dry-run and explicit overwrite preconditions. After starting the game's own integration, `aibt_runtime_hosts` identifies hosts and `aibt_read_trace` reads bounded native history without completing jobs. For a simulation container the game registered (TreeSimulationDebug.Register), `aibt_runtime_simulations` lists containers, groups and traced agents, `aibt_describe_agent` explains one agent (active path, last/next think and why, result, fault, live actions) and `aibt_read_agent_trace` reads a traced agent's trace with ticks; all three are read-only and answer busy during a step. Reference compile/simulate are separate from native preparation/execution. `aibt_migrate_document` persists registered document migrations. Project documentation is generated under Assets/AIBT-Documentation; runtime resources resolve the installed package root.
