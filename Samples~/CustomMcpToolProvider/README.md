# Custom MCP tool provider sample

This sample registers one real, working `AIBT.Mcp.CustomTools.ICustomMcpToolProvider` --
`aibt_sample_greeting` (`P6-010`'s own IoC extension point,
`Documentation~/ai-and-mcp.md`'s "Custom MCP tools" section). It declares a stable name,
description, JSON input/output schemas, permission category, side effects, cancellation support,
and dry-run support -- the full contract a custom tool must supply -- then echoes a greeting back
for whatever `name` argument it receives.

Import the sample through Unity Package Manager; no internal `AIBT.Runtime`/`AIBT.Mcp` API is
required. Once imported, the tool is discoverable automatically: `AIBT.Mcp.CustomTools
.CustomMcpToolProviderDiscovery` finds every `ICustomMcpToolProvider` implementation via
`UnityEditor.TypeCache` when the MCP bridge attaches -- no separate registration call, no AIBT
assembly ever references this one directly. Call it like any other tool through a connected MCP
client (`tools/list` will show `aibt_sample_greeting` alongside AIBT's own built-ins;
`tools/call` with `{"name": "..."}` returns a greeting).

A project builds its own tools the same way: implement the interface, declare the real contract
(schemas, permission category, side effects), and import/ship the assembly -- the discovery
mechanism does the rest.
