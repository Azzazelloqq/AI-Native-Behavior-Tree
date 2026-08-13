# Security policy

Do not open a public issue for a suspected vulnerability. Use GitHub private vulnerability reporting when available, or contact the maintainer through the address published in `package.json`.

Include affected versions, reproduction steps, impact, and any proposed mitigation. Never include credentials, private project data, or destructive proof-of-concept payloads.

The MCP and code-generation layers must treat external input as untrusted. Generated changes require validation, reviewable diffs, compilation, tests, and explicit application. Secrets and credentials must never be written into tree, layout, policy, trace, or benchmark files.

