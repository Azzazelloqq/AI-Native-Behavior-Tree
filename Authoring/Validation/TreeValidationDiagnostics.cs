using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    public static class TreeValidationDiagnosticCodes
    {
        public static readonly DiagnosticCode InvalidDocument = new DiagnosticCode("AIBT2010");
        public static readonly DiagnosticCode InvalidFormat = new DiagnosticCode("AIBT2011");
        public static readonly DiagnosticCode UnsupportedFormatVersion = new DiagnosticCode("AIBT2012");
        public static readonly DiagnosticCode InvalidTreeIdentity = new DiagnosticCode("AIBT2013");
        public static readonly DiagnosticCode MissingTreeName = new DiagnosticCode("AIBT2014");
        public static readonly DiagnosticCode InvalidRoot = new DiagnosticCode("AIBT2015");
        public static readonly DiagnosticCode InvalidNodeIdentity = new DiagnosticCode("AIBT2016");
        public static readonly DiagnosticCode DuplicateNodeIdentity = new DiagnosticCode("AIBT2017");
        public static readonly DiagnosticCode MissingChild = new DiagnosticCode("AIBT2018");
        public static readonly DiagnosticCode Cycle = new DiagnosticCode("AIBT2019");
        public static readonly DiagnosticCode UnreachableNode = new DiagnosticCode("AIBT2020");
        public static readonly DiagnosticCode UnknownNodeType = new DiagnosticCode("AIBT2021");
        public static readonly DiagnosticCode UnsupportedNodeVersion = new DiagnosticCode("AIBT2022");
        public static readonly DiagnosticCode ChildPolicy = new DiagnosticCode("AIBT2023");
        public static readonly DiagnosticCode DuplicateChild = new DiagnosticCode("AIBT2024");
        public static readonly DiagnosticCode UnknownParameter = new DiagnosticCode("AIBT2025");
        public static readonly DiagnosticCode MissingParameter = new DiagnosticCode("AIBT2026");
        public static readonly DiagnosticCode ParameterType = new DiagnosticCode("AIBT2027");
        public static readonly DiagnosticCode ParameterConstraint = new DiagnosticCode("AIBT2028");
        public static readonly DiagnosticCode DuplicateParameter = new DiagnosticCode("AIBT2029");
        public static readonly DiagnosticCode UnsupportedBlackboardScope = new DiagnosticCode("AIBT2030");
        public static readonly DiagnosticCode MissingBlackboardAccess = new DiagnosticCode("AIBT2031");
        public static readonly DiagnosticCode UnsupportedBlackboardWrite = new DiagnosticCode("AIBT2032");
        public static readonly DiagnosticCode InvalidObserverMode = new DiagnosticCode("AIBT2033");
        public static readonly DiagnosticCode InvalidObserverContext = new DiagnosticCode("AIBT2034");
        public static readonly DiagnosticCode InvalidWatchedKey = new DiagnosticCode("AIBT2035");
        public static readonly DiagnosticCode DuplicateWatchedKey = new DiagnosticCode("AIBT2036");
        public static readonly DiagnosticCode ParallelThreshold = new DiagnosticCode("AIBT2037");
        public static readonly DiagnosticCode PolicyViolation = new DiagnosticCode("AIBT2038");
        public static readonly DiagnosticCode UnsupportedExecutionDomain = new DiagnosticCode("AIBT2039");
        public static readonly DiagnosticCode UnsupportedNodeCapability = new DiagnosticCode("AIBT2040");
        public static readonly DiagnosticCode MultipleParents = new DiagnosticCode("AIBT2041");

        /// <summary>
        /// P7-006 (ADR-P7-005): a node's authored parameters were rewritten in memory by a
        /// registered migration rule chain to reach the currently-registered manifest version.
        /// Info severity -- the document already compiles/validates successfully; this is a
        /// heads-up for review, never a blocking failure.
        /// </summary>
        public static readonly DiagnosticCode MigrationApplied = new DiagnosticCode("AIBT2042");
    }

    public static class TreeValidationDiagnosticCatalog
    {
        private const DiagnosticField RequiredFields = DiagnosticField.JsonPointer;
        private const DiagnosticField OptionalFields = DiagnosticField.DocumentId
            | DiagnosticField.TreeId
            | DiagnosticField.NodeId
            | DiagnosticField.RelatedLocations;

        private static readonly DiagnosticCode[] Codes =
        {
            TreeValidationDiagnosticCodes.InvalidDocument,
            TreeValidationDiagnosticCodes.InvalidFormat,
            TreeValidationDiagnosticCodes.UnsupportedFormatVersion,
            TreeValidationDiagnosticCodes.InvalidTreeIdentity,
            TreeValidationDiagnosticCodes.MissingTreeName,
            TreeValidationDiagnosticCodes.InvalidRoot,
            TreeValidationDiagnosticCodes.InvalidNodeIdentity,
            TreeValidationDiagnosticCodes.DuplicateNodeIdentity,
            TreeValidationDiagnosticCodes.MissingChild,
            TreeValidationDiagnosticCodes.Cycle,
            TreeValidationDiagnosticCodes.UnreachableNode,
            TreeValidationDiagnosticCodes.UnknownNodeType,
            TreeValidationDiagnosticCodes.UnsupportedNodeVersion,
            TreeValidationDiagnosticCodes.ChildPolicy,
            TreeValidationDiagnosticCodes.DuplicateChild,
            TreeValidationDiagnosticCodes.UnknownParameter,
            TreeValidationDiagnosticCodes.MissingParameter,
            TreeValidationDiagnosticCodes.ParameterType,
            TreeValidationDiagnosticCodes.ParameterConstraint,
            TreeValidationDiagnosticCodes.DuplicateParameter,
            TreeValidationDiagnosticCodes.UnsupportedBlackboardScope,
            TreeValidationDiagnosticCodes.MissingBlackboardAccess,
            TreeValidationDiagnosticCodes.UnsupportedBlackboardWrite,
            TreeValidationDiagnosticCodes.InvalidObserverMode,
            TreeValidationDiagnosticCodes.InvalidObserverContext,
            TreeValidationDiagnosticCodes.InvalidWatchedKey,
            TreeValidationDiagnosticCodes.DuplicateWatchedKey,
            TreeValidationDiagnosticCodes.ParallelThreshold,
            TreeValidationDiagnosticCodes.PolicyViolation,
            TreeValidationDiagnosticCodes.UnsupportedExecutionDomain,
            TreeValidationDiagnosticCodes.UnsupportedNodeCapability,
            TreeValidationDiagnosticCodes.MultipleParents,
            TreeValidationDiagnosticCodes.MigrationApplied,
        };

        public static DiagnosticCatalog Catalog { get; } = CreateCatalog();

        internal static Diagnostic Create(
            DiagnosticCode code,
            string message,
            DiagnosticLocation location,
            IEnumerable<DiagnosticLocation> relatedLocations = null,
            DiagnosticSeverity? severity = null)
        {
            if (!Catalog.TryGet(code, out var descriptor))
            {
                throw new ArgumentException("The code is not registered in the tree validation catalog.", nameof(code));
            }

            return new Diagnostic(code, severity ?? descriptor.DefaultSeverity, message, location, relatedLocations);
        }

        private static DiagnosticCatalog CreateCatalog()
        {
            var descriptors = new DiagnosticDescriptor[Codes.Length];
            for (var index = 0; index < Codes.Length; index++)
            {
                // Every code defaults to Error except MigrationApplied: a migration having been
                // applied means the document already compiles/validates successfully, so it is
                // never a blocking failure -- only Info, a review heads-up (ADR-P7-005).
                var defaultSeverity = Codes[index] == TreeValidationDiagnosticCodes.MigrationApplied
                    ? DiagnosticSeverity.Info
                    : DiagnosticSeverity.Error;
                descriptors[index] = new DiagnosticDescriptor(
                    Codes[index],
                    DiagnosticSubsystem.SemanticValidation,
                    defaultSeverity,
                    RequiredFields,
                    OptionalFields);
            }

            return new DiagnosticCatalog(descriptors);
        }
    }
}
