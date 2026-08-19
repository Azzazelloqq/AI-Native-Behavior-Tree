namespace AIBT.Editor.Layout
{
    /// <summary>Result of <see cref="CanonicalLayoutJson.Parse(byte[], string, AIBT.Authoring.TreeDocument)"/>.</summary>
    public sealed class LayoutJsonReadResult
    {
        public LayoutJsonReadResult(LayoutDocument document, DiagnosticCollection diagnostics, string sourceText, byte[] sourceUtf8)
        {
            Document = document;
            Diagnostics = diagnostics;
            SourceText = sourceText;
            SourceUtf8 = sourceUtf8;
        }

        public LayoutDocument Document { get; }

        public DiagnosticCollection Diagnostics { get; }

        public string SourceText { get; }

        public byte[] SourceUtf8 { get; }

        public bool Success => Document != null && Diagnostics.Count == 0;
    }
}
