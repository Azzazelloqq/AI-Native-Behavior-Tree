# Agent decision boundaries

Complete autonomy does not mean inventing product behavior. These boundaries define what an assigned agent may decide locally.

## May decide without escalation

- private method and local variable names;
- a private algorithm that satisfies all complexity, determinism, allocation, and observable-behavior requirements;
- private test helpers and fixture organization inside owned paths;
- refactoring wholly inside owned paths when it does not change public/internal cross-task contracts;
- clearer diagnostic message wording when stable code, location, and structured fields remain unchanged.

## Must escalate before implementation

- public or cross-assembly API shape;
- persisted JSON/schema or compiled-format changes;
- lifecycle, status, abort, ordering, timing, or deterministic behavior;
- new package dependency, assembly reference, platform conditional, reflection, unsafe code, or managed fallback;
- new diagnostic code range or changed diagnostic severity contract;
- ownership changes or edits outside allowed paths;
- performance thresholds, Auto scheduler defaults, or supported-platform claims;
- any weakening of tests, policies, or Definition of Done.

## Research tasks

A spike may compare options named by its card and recommend one. It may not merge the recommendation into production contracts. A separate accepted decision converts evidence into architecture.

## Missing detail

If a detail affects only private implementation and every compliant choice is observably equivalent, choose the simplest correct option and record it in the handoff. Otherwise stop and request a decision.
