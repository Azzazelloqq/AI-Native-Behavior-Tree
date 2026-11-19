namespace AIBT.Authoring
{
    /// <summary>
    /// ADR-P11-001 §4.6: the subtree expansion rules that need the referenced document (compiler
    /// range). Every code is an error and is registered in the reference compiler's catalog.
    /// </summary>
    public static class SubtreeExpansionDiagnosticCodes
    {
        /// <summary>No document has the referenced tree ID, or no resolver was given and the tree has references.</summary>
        public static readonly DiagnosticCode SubtreeNotFound = new DiagnosticCode("AIBT3020");

        /// <summary>More than one document has the referenced tree ID (related locations list them).</summary>
        public static readonly DiagnosticCode SubtreeAmbiguous = new DiagnosticCode("AIBT3021");

        /// <summary>The reference graph has a cycle (the message lists it).</summary>
        public static readonly DiagnosticCode SubtreeCycle = new DiagnosticCode("AIBT3022");

        /// <summary>Subtrees are nested deeper than <see cref="SubtreeExpander.MaximumNestingDepth"/>.</summary>
        public static readonly DiagnosticCode SubtreeDepth = new DiagnosticCode("AIBT3023");

        /// <summary>
        /// <c>interfaceVersion</c> differs from the subtree's <c>interface.version</c>, or the
        /// subtree has no interface but the reference binds keys.
        /// </summary>
        public static readonly DiagnosticCode SubtreeInterfaceVersion = new DiagnosticCode("AIBT3024");

        /// <summary>Missing or unknown entries in <c>subtree.keys</c>.</summary>
        public static readonly DiagnosticCode SubtreeKeySet = new DiagnosticCode("AIBT3025");

        /// <summary>Type/version/enum mismatch of a binding or constant; a fixed-name manifest access that cannot be renamed.</summary>
        public static readonly DiagnosticCode SubtreeKeyType = new DiagnosticCode("AIBT3026");

        /// <summary>An expanded node or key ID exceeds 128 characters or collides with another.</summary>
        public static readonly DiagnosticCode SubtreeQualifiedIdentity = new DiagnosticCode("AIBT3027");

        /// <summary>The subtree's Agent/Shared scope contract differs from or is missing in the parent.</summary>
        public static readonly DiagnosticCode SubtreeScopeContract = new DiagnosticCode("AIBT3028");
    }
}
