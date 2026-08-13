# Validation workflow

`validation.yml` is the Phase 0 Windows validation workflow.

The static job uses a GitHub-hosted `windows-2022` runner and executes the repository-owned `Verify-Static.ps1` entrypoint. The Unity job uses a self-hosted Windows x64 runner labelled `unity-6000.5.8f1`. It creates an isolated Unity project, embeds the checked-out AIBT package, and invokes the repository-owned compile and full EditMode entrypoints.

The self-hosted runner requirements are:

- Unity `6000.5.8f1` is installed and already activated outside the workflow;
- runner environment variable `UNITY_EDITOR_PATH` names that Editor executable;
- labels `self-hosted`, `Windows`, `X64`, and `unity-6000.5.8f1` are present;
- the runner account may create the workspace-local Unity `Library` and verification output.

The workflow does not accept, read, or persist a Unity license, activation response, serial, username, or password. Rotation and activation remain runner-administration operations. Pull requests from forks therefore never receive license material.

Action revisions and Python tooling are pinned. The Unity cache key includes the baseline Editor version, package dependency declaration, baseline harness manifest, and project version. Uploaded Unity artifacts are copied through a sanitizer that replaces workspace and Editor installation paths.
