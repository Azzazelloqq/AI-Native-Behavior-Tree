using Unity.Burst;
using Unity.Jobs;

namespace AIBT
{
    [BurstCompile]
    internal struct NativeChannelsBurstProbeJobV1 : IJob
    {
        internal NativeDiagnosticWriterV1 DiagnosticWriter;
        internal NativeDiagnosticRecordV1 Diagnostic;
        internal NativeTraceWriterV1 TraceWriter;
        internal NativeTraceRecordV1 Trace;

        public void Execute()
        {
            DiagnosticWriter.TryAppend(Diagnostic);
            TraceWriter.TryAppend(Trace);
        }
    }
}
