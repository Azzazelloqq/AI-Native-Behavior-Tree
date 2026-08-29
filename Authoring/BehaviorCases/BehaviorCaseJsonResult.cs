using System;

namespace AIBT.Authoring.BehaviorCases
{
    internal sealed class BehaviorCaseJsonReadResult
    {
        internal BehaviorCaseJsonReadResult(BehaviorCaseDocument document, DiagnosticCollection diagnostics, string source)
        {
            Document = document;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            Source = source;
            if ((document == null) == (diagnostics.Count == 0))
                throw new ArgumentException("A read result must contain exactly one of a document or diagnostics.");
        }
        internal BehaviorCaseDocument Document { get; }
        internal DiagnosticCollection Diagnostics { get; }
        internal string Source { get; }
        internal bool Success => Document != null;
    }

    internal sealed class BehaviorCaseJsonWriteResult
    {
        internal BehaviorCaseJsonWriteResult(byte[] utf8, DiagnosticCollection diagnostics)
        {
            Utf8 = utf8 == null ? null : (byte[])utf8.Clone();
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            if ((utf8 == null) == (diagnostics.Count == 0))
                throw new ArgumentException("A write result must contain exactly one of bytes or diagnostics.");
        }
        private byte[] Utf8 { get; }
        internal DiagnosticCollection Diagnostics { get; }
        internal bool Success => Utf8 != null;
        internal byte[] CopyUtf8() => Utf8 == null ? null : (byte[])Utf8.Clone();
    }

    internal sealed class BehaviorCaseJsonReadException : Exception
    {
        internal BehaviorCaseJsonReadException(Diagnostic diagnostic) { Diagnostic = diagnostic; }
        internal Diagnostic Diagnostic { get; }
    }
}
