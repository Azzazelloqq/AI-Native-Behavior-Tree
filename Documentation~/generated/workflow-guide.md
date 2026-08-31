# AIBT MCP agent workflow guide (generated)

Reflects the actual registered MCP tools (see `AIBT.Mcp.McpBuiltInTools`), not an idealized set. Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command.

## 1. Connect

Start the Unity-side bridge from the open Editor (`AIBT/MCP/Bridge` -> Start), then launch the AI client with the external server configured (`dotnet run --project <path to>/MCP~/Server`), per `ADR-P6-001`. The client and the Editor must be on the same machine; the bridge must be running before the server process starts.

## 2. Discover

Call `aibt_get_project_manifest` for capabilities, project policy, and the tree/revision listing; `aibt_search_nodes` to search the node catalog by keyword; `aibt_get_node_contract` for one node type's full contract. Also see the generated node catalog document for every node's contract at once.

## 3. Author

Create a tree with `aibt_create_tree`, then edit it with `aibt_add_node`, `aibt_remove_node`, `aibt_move_node`, `aibt_replace_node`, `aibt_configure_node`, or `aibt_set_blackboard_keys`. Use `aibt_extract_subtree`/`aibt_inline_subtree` to move a subtree between trees, `aibt_apply_domain_patch` to apply several operations as one atomic transaction, and `aibt_request_layout` afterward to lay out the affected region. Every mutating call takes the target's current `expectedHash`/`contentHash` -- always use the value the last accepted call returned, never assume a fixed increment (`ADR-P6-002`).

## 4. Verify

Run `aibt_validate` and `aibt_compile` against a tree, `aibt_simulate` to step it through the Phase 1 reference executor, `aibt_run_tests` against a `.aibtcase.json` behavior case, and `aibt_run_benchmark` against a real P4-001 scheduling scenario. Use `aibt_explain_diagnostic` to look up a returned diagnostic code's stable meaning.

## 5. Add a custom node

The generate-compile-apply gate: `aibt_generate_node` (stages a new node from a template), `aibt_preview_node_diff` (inspect the staged source before compiling), `aibt_generate_node_tests_and_manifest` (stages a paired test scaffold), `aibt_analyze_and_compile_node` (two-call, non-blocking compile check -- call with `mode='start'`, then poll with `mode='check'`), `aibt_test_node` (proves the compiled shard is registry-materializable), and finally `aibt_apply_node` (the only step that persists into the real project). Nothing before `apply_node` touches the real project.

## 6. Custom project tools

Call `aibt_list_custom_tools` to discover any project-registered custom tools, and `aibt_call_custom_tool` to invoke one by name.
