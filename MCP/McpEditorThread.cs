using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace AIBT.Mcp
{
    [InitializeOnLoad]
    internal static class McpEditorThread
    {
        private static readonly int ThreadId = Thread.CurrentThread.ManagedThreadId;
        private static readonly ConcurrentQueue<Action> Pending = new ConcurrentQueue<Action>();

        static McpEditorThread() => EditorApplication.update += Pump;

        internal static T Invoke<T>(Func<T> operation)
        {
            if (Thread.CurrentThread.ManagedThreadId == ThreadId) return operation();
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Enqueue(() =>
            {
                if (completion.Task.IsCompleted) return;
                try { completion.TrySetResult(operation()); }
                catch (Exception exception) { completion.TrySetException(exception); }
            });
            if (Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(15))).GetAwaiter().GetResult() != completion.Task)
            {
                completion.TrySetCanceled();
                throw new InvalidOperationException("Editor is busy; request timed out. If execution had already begun, inspect its outcome before retrying a write.");
            }
            return completion.Task.GetAwaiter().GetResult();
        }

        private static void Pump()
        {
            while (Pending.TryDequeue(out var operation)) operation();
        }
    }
}
