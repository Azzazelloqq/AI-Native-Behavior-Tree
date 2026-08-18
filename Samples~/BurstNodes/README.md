# Public Burst-node sample

This sample contains two nodes authored only against the public `AIBT.Runtime`
API:

- `ThresholdConditionNode` performs a typed blackboard read and can also be used
  as an observer condition.
- `AsyncWriteActionNode` performs typed read/write, emits a command, returns
  `Running` while an asynchronous operation is pending, consumes its completion,
  and cancels the operation from `Abort`.

The shard and catalog live in separate assemblies because catalog generation
consumes the already generated shard authority. Import the sample through Unity
Package Manager; no internal Runtime API or managed fallback is required.
