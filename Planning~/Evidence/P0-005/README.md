# P0-005 Windows validation CI evidence

Observed locally on 2026-08-14.

## Implemented workflow

- Provider: GitHub Actions.
- Static/schema job: GitHub-hosted `windows-2022` runner.
- Unity compile and full EditMode job: pre-activated self-hosted Windows x64 runner labelled `unity-6000.5.8f1`.
- Unity activation data is runner-owned and never enters workflow inputs, secrets, logs, caches, or artifacts.
- Repository-owned PowerShell entrypoints remain the commands executed by CI.
- Third-party actions and `uv` are pinned.
- Unity cache inputs include the Editor baseline and package/harness dependency declarations.
- Logs and test results are sanitized before upload and retained for 14 days.
- An invalid focused-test invocation is required to return nonzero as a controlled failure check.

## Local verification

- Workflow YAML parse: pass.
- Repository static/schema verification: pass.
- `git diff --check`: pass.
- Runtime compile after the concurrent P1 fixture correction: pass with zero errors and three known Unity/.NET assembly-version warnings.

Machine-readable results are in `local-verification.json`.

## Pending acceptance evidence

The workflow has not yet run on GitHub. P0-005 remains `Review` until both jobs pass in a repository workflow run. This local environment cannot manufacture that external run, runner labels, or GitHub check conclusion.
