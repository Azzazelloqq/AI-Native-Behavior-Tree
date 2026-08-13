using System;

namespace AIBT.Authoring
{
    public sealed class TreeJsonReadResult
    {
        internal TreeJsonReadResult(TreeDocument document, DiagnosticCollection diagnostics, string sourceText, byte[] sourceUtf8)
        {
            Document = document;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            SourceText = sourceText;
            SourceUtf8 = sourceUtf8 == null ? null : (byte[])sourceUtf8.Clone();
        }

        public TreeDocument Document { get; }

        public DiagnosticCollection Diagnostics { get; }

        public string SourceText { get; }

        public byte[] SourceUtf8 { get; }

        public bool Success => Document != null && Diagnostics.Count == 0;
    }

    public sealed class TreeJsonWriteResult
    {
        internal TreeJsonWriteResult(byte[] utf8, byte[] semanticHash, DiagnosticCollection diagnostics)
        {
            Utf8 = utf8 == null ? null : (byte[])utf8.Clone();
            SemanticHash = semanticHash == null ? null : (byte[])semanticHash.Clone();
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public byte[] Utf8 { get; }

        public byte[] SemanticHash { get; }

        public DiagnosticCollection Diagnostics { get; }

        public bool Success => Utf8 != null && Diagnostics.Count == 0;
    }
}
