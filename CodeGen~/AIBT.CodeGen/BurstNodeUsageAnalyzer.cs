using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AIBT.CodeGen
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class BurstNodeUsageAnalyzer : DiagnosticAnalyzer
    {
        private const string NodeAttribute = "AIBT.Burst.AibtBurstNodeAttribute";
        private const string RandomAttribute = "AIBT.Burst.AibtRandomStreamAttribute";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(AbiDiagnostics.UndeclaredAccess, AbiDiagnostics.WrongAccess, AbiDiagnostics.Forbidden);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeForbiddenSymbol,
                SyntaxKind.SimpleMemberAccessExpression, SyntaxKind.IdentifierName, SyntaxKind.QualifiedName, SyntaxKind.TypeOfExpression);
            context.RegisterSyntaxNodeAction(AnalyzeAllocation,
                SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression,
                SyntaxKind.ArrayCreationExpression, SyntaxKind.ImplicitArrayCreationExpression,
                SyntaxKind.AnonymousObjectCreationExpression, SyntaxKind.SimpleLambdaExpression,
                SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.AnonymousMethodExpression,
                SyntaxKind.AwaitExpression, SyntaxKind.YieldReturnStatement, SyntaxKind.YieldBreakStatement,
                SyntaxKind.StackAllocArrayCreationExpression, SyntaxKind.PointerIndirectionExpression,
                SyntaxKind.AddressOfExpression, SyntaxKind.StringLiteralExpression);
            context.RegisterCompilationAction(AnalyzeForbiddenExceptionFlow);
            context.RegisterCompilationAction(AnalyzeForbiddenApiFlow);
            context.RegisterCompilationAction(AnalyzeForbiddenCapabilityFlow);
        }

        internal static bool HasInvalidUsage(Compilation compilation)
        {
            if (ForbiddenExceptionFlow(compilation).Count != 0
                || ForbiddenApiFlow(compilation).Count != 0
                || ForbiddenCapabilityFlow(compilation).Count != 0) return true;
            foreach (var tree in compilation.SyntaxTrees)
            {
#pragma warning disable RS1030 // Generator-side atomicity audit intentionally uses the compilation semantic model.
                var model = compilation.GetSemanticModel(tree);
#pragma warning restore RS1030
                foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var callback = model.GetDeclaredSymbol(declaration) as IMethodSymbol;
                    if (callback == null || !HasAttribute(callback.ContainingType, NodeAttribute)) continue;
                    foreach (var node in declaration.DescendantNodes())
                    {
                        if (node is InvocationExpressionSyntax invocation && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol called)
                        {
                            if (called.ContainingType?.Name == "BurstAccess")
                            {
                                var expected = BindingAttribute(called.Name);
                                if (expected != null && !ValidBinding(model, invocation, called, callback, expected, 1)) return true;
                            }
                            else
                            {
                                var contextName = called.ContainingType?.ToDisplayString();
                                if (contextName != null && contextName.StartsWith("AIBT.Burst.Burst", StringComparison.Ordinal) && contextName.EndsWith("Context", StringComparison.Ordinal))
                                {
                                    if ((called.Name == "TryNextUInt32" || called.Name == "TryNextFloat32") && !HasAttribute(callback.ContainingType, RandomAttribute)) return true;
                                    if (called.Name.StartsWith("TryBegin", StringComparison.Ordinal)) return true;
                                }
                            }
                        }
                        else if (node is InvocationExpressionSyntax unresolved && unresolved.Expression.ToString().IndexOf(".BurstAccess.", StringComparison.Ordinal) >= 0)
                        {
                            var methodName = (unresolved.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText ?? string.Empty;
                            var expected = BindingAttribute(methodName);
                            if (expected != null && !ValidUnresolvedBinding(model, unresolved, callback, methodName, expected, 1)) return true;
                        }
                        if (node is ObjectCreationExpressionSyntax || node is ImplicitObjectCreationExpressionSyntax || node is ArrayCreationExpressionSyntax
                            || node is ImplicitArrayCreationExpressionSyntax || node is AnonymousObjectCreationExpressionSyntax || node is LambdaExpressionSyntax
                            || node is AnonymousMethodExpressionSyntax || node is AwaitExpressionSyntax || node is YieldStatementSyntax
                            || node is StackAllocArrayCreationExpressionSyntax || node is PrefixUnaryExpressionSyntax prefix && (prefix.IsKind(SyntaxKind.AddressOfExpression) || prefix.IsKind(SyntaxKind.PointerIndirectionExpression))
                            || node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                        {
                            var type = model.GetTypeInfo(node).Type;
                            if (!(node is ObjectCreationExpressionSyntax || node is ImplicitObjectCreationExpressionSyntax) || type == null || !type.IsValueType || !type.IsUnmanagedType) return true;
                        }
                        var symbol = ResolvedSymbol(model, node);
                        if (TryForbiddenApi(symbol, model.GetTypeInfo(node).Type, out _)) return true;
                    }
                }
            }
            return false;
        }

        private static void AnalyzeForbiddenExceptionFlow(CompilationAnalysisContext context)
        {
            foreach (var usage in ForbiddenExceptionFlow(context.Compilation))
                context.ReportDiagnostic(Diagnostic.Create(AbiDiagnostics.Forbidden, usage.Location, usage.Display));
        }

        private static void AnalyzeForbiddenApiFlow(CompilationAnalysisContext context)
        {
            foreach (var usage in ForbiddenApiFlow(context.Compilation))
                context.ReportDiagnostic(Diagnostic.Create(AbiDiagnostics.Forbidden, usage.Location, usage.Display));
        }

        private static void AnalyzeForbiddenCapabilityFlow(CompilationAnalysisContext context)
        {
            foreach (var usage in ForbiddenCapabilityFlow(context.Compilation))
                context.ReportDiagnostic(Diagnostic.Create(
                    AbiDiagnostics.UndeclaredAccess,
                    usage.Location,
                    usage.Display));
        }

        private static List<ForbiddenExceptionUsage> ForbiddenCapabilityFlow(Compilation compilation)
        {
            var roots = new List<IMethodSymbol>();
            foreach (var tree in compilation.SyntaxTrees)
            {
#pragma warning disable RS1030 // Closed source call-graph analysis requires semantic models for callback declarations.
                var model = compilation.GetSemanticModel(tree);
#pragma warning restore RS1030
                foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var method = model.GetDeclaredSymbol(declaration) as IMethodSymbol;
                    if (method != null && IsCallbackRoot(method)) roots.Add(method);
                }
            }

            roots.Sort((left, right) => string.CompareOrdinal(SymbolOrder(left), SymbolOrder(right)));
            var pending = new Queue<IMethodSymbol>(roots);
            var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var locations = new HashSet<(SyntaxTree Tree, int Start, int Length)>();
            var result = new List<ForbiddenExceptionUsage>();
            while (pending.Count != 0)
            {
                var method = pending.Dequeue();
                if (!visited.Add(method)) continue;
                var reportedBySyntaxAction = HasAttribute(method.ContainingType, NodeAttribute);
                foreach (var syntaxReference in method.DeclaringSyntaxReferences.OrderBy(
                    reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(reference => reference.Span.Start))
                {
                    var declaration = syntaxReference.GetSyntax();
#pragma warning disable RS1030 // Closed source call-graph analysis requires the declaring tree semantic model.
                    var model = compilation.GetSemanticModel(declaration.SyntaxTree);
#pragma warning restore RS1030
                    foreach (var node in CallableBodyNodes(declaration))
                    {
                        if (node is InvocationExpressionSyntax invocation)
                        {
                            var called = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                            if (TryRestrictedCapabilityInvocation(invocation, called, out var operation))
                            {
                                if (!reportedBySyntaxAction)
                                {
                                    var location = invocation.GetLocation();
                                    var key = (location.SourceTree!, location.SourceSpan.Start, location.SourceSpan.Length);
                                    if (locations.Add(key))
                                        result.Add(new ForbiddenExceptionUsage(location, operation));
                                }
                                continue;
                            }
                        }

                        foreach (var called in ReferencedCallables(model, node))
                            if (called.DeclaringSyntaxReferences.Length != 0)
                                pending.Enqueue(called.ReducedFrom ?? called.OriginalDefinition);
                    }
                }
            }

            result.Sort((left, right) =>
            {
                var comparison = string.CompareOrdinal(left.Location.SourceTree?.FilePath, right.Location.SourceTree?.FilePath);
                return comparison != 0 ? comparison : left.Location.SourceSpan.Start.CompareTo(right.Location.SourceSpan.Start);
            });
            return result;
        }

        private static IEnumerable<IMethodSymbol> ReferencedCallables(
            SemanticModel model,
            SyntaxNode node)
        {
            IMethodSymbol? method = null;
            if (node is InvocationExpressionSyntax
                || node is ObjectCreationExpressionSyntax
                || node is ImplicitObjectCreationExpressionSyntax
                || node is BinaryExpressionSyntax
                || node is PrefixUnaryExpressionSyntax
                || node is PostfixUnaryExpressionSyntax
                || node is AssignmentExpressionSyntax
                || node is CastExpressionSyntax)
            {
                method = model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            }

            if (method != null)
                yield return method;

            if (!(node is MemberAccessExpressionSyntax)
                && !(node is IdentifierNameSyntax)
                && !(node is ElementAccessExpressionSyntax)
                && !(node is MemberBindingExpressionSyntax))
            {
                yield break;
            }

            var property = model.GetSymbolInfo(node).Symbol as IPropertySymbol;
            if (property == null)
                yield break;

            var assignment = node.AncestorsAndSelf().OfType<AssignmentExpressionSyntax>()
                .FirstOrDefault(candidate => candidate.Left.Span.Contains(node.Span));
            var mutation = node.AncestorsAndSelf().FirstOrDefault(candidate =>
                (candidate is PrefixUnaryExpressionSyntax prefix
                    && (prefix.IsKind(SyntaxKind.PreIncrementExpression)
                        || prefix.IsKind(SyntaxKind.PreDecrementExpression))
                 || candidate is PostfixUnaryExpressionSyntax postfix
                    && (postfix.IsKind(SyntaxKind.PostIncrementExpression)
                        || postfix.IsKind(SyntaxKind.PostDecrementExpression)))
                && candidate.Span.Contains(node.Span));
            var reads = assignment == null
                || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                || mutation != null;
            var writes = assignment != null || mutation != null;
            if (reads && property.GetMethod != null)
                yield return property.GetMethod;
            if (writes && property.SetMethod != null)
                yield return property.SetMethod;
        }

        private static bool TryRestrictedCapabilityInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? called,
            out string operation)
        {
            operation = called?.Name
                ?? (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText
                ?? string.Empty;
            if (called?.ContainingType?.Name == "BurstAccess" && BindingAttribute(operation) != null)
                return true;
            if (called == null
                && invocation.Expression.ToString().IndexOf(".BurstAccess.", StringComparison.Ordinal) >= 0
                && BindingAttribute(operation) != null)
                return true;

            var contextName = called?.ContainingType?.ToDisplayString();
            return contextName != null
                && contextName.StartsWith("AIBT.Burst.Burst", StringComparison.Ordinal)
                && contextName.EndsWith("Context", StringComparison.Ordinal)
                && (operation.StartsWith("TryBegin", StringComparison.Ordinal)
                    || operation == "TryNextUInt32"
                    || operation == "TryNextFloat32");
        }

        private static List<ForbiddenExceptionUsage> ForbiddenApiFlow(Compilation compilation)
        {
            var roots = new List<IMethodSymbol>();
            foreach (var tree in compilation.SyntaxTrees)
            {
#pragma warning disable RS1030 // Closed source call-graph analysis requires semantic models for callback declarations.
                var model = compilation.GetSemanticModel(tree);
#pragma warning restore RS1030
                foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var method = model.GetDeclaredSymbol(declaration) as IMethodSymbol;
                    if (method != null && IsCallbackRoot(method)) roots.Add(method);
                }
            }

            roots.Sort((left, right) => string.CompareOrdinal(SymbolOrder(left), SymbolOrder(right)));
            var pending = new Queue<IMethodSymbol>(roots);
            var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var locations = new HashSet<(SyntaxTree Tree, int Start, int Length)>();
            var result = new List<ForbiddenExceptionUsage>();
            while (pending.Count != 0)
            {
                var method = pending.Dequeue();
                if (!visited.Add(method)) continue;
                var reportedBySyntaxAction = HasAttribute(method.ContainingType, NodeAttribute);
                foreach (var syntaxReference in method.DeclaringSyntaxReferences.OrderBy(
                    reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(reference => reference.Span.Start))
                {
                    var declaration = syntaxReference.GetSyntax();
#pragma warning disable RS1030 // Closed source call-graph analysis requires the declaring tree semantic model.
                    var model = compilation.GetSemanticModel(declaration.SyntaxTree);
#pragma warning restore RS1030
                    foreach (var node in CallableBodyNodes(declaration))
                    {
                        if (!reportedBySyntaxAction && TryForbiddenApiUsage(model, node, out var usageNode, out var display))
                        {
                            var location = usageNode.GetLocation();
                            var key = (location.SourceTree!, location.SourceSpan.Start, location.SourceSpan.Length);
                            if (locations.Add(key)) result.Add(new ForbiddenExceptionUsage(location, display));
                        }

                        IMethodSymbol? called = null;
                        if (node is InvocationExpressionSyntax invocation)
                            called = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                        else if (node is ObjectCreationExpressionSyntax creation)
                            called = model.GetSymbolInfo(creation).Symbol as IMethodSymbol;
                        else if (node is ImplicitObjectCreationExpressionSyntax implicitCreation)
                            called = model.GetSymbolInfo(implicitCreation).Symbol as IMethodSymbol;
                        if (called != null && called.DeclaringSyntaxReferences.Length != 0)
                            pending.Enqueue(called.ReducedFrom ?? called.OriginalDefinition);
                    }
                }
            }
            result.Sort((left, right) =>
            {
                var comparison = string.CompareOrdinal(left.Location.SourceTree?.FilePath, right.Location.SourceTree?.FilePath);
                return comparison != 0 ? comparison : left.Location.SourceSpan.Start.CompareTo(right.Location.SourceSpan.Start);
            });
            return result;
        }

        private static List<ForbiddenExceptionUsage> ForbiddenExceptionFlow(Compilation compilation)
        {
            var roots = new List<IMethodSymbol>();
            foreach (var tree in compilation.SyntaxTrees)
            {
#pragma warning disable RS1030 // Closed source call-graph analysis requires semantic models for callback declarations.
                var model = compilation.GetSemanticModel(tree);
#pragma warning restore RS1030
                foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var method = model.GetDeclaredSymbol(declaration) as IMethodSymbol;
                    if (method != null && IsCallbackRoot(method)) roots.Add(method);
                }
            }

            roots.Sort((left, right) => string.CompareOrdinal(SymbolOrder(left), SymbolOrder(right)));
            var pending = new Queue<IMethodSymbol>(roots);
            var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var locations = new HashSet<(SyntaxTree Tree, int Start, int Length)>();
            var result = new List<ForbiddenExceptionUsage>();
            while (pending.Count != 0)
            {
                var method = pending.Dequeue();
                if (!visited.Add(method)) continue;
                foreach (var syntaxReference in method.DeclaringSyntaxReferences.OrderBy(
                    reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(reference => reference.Span.Start))
                {
                    var declaration = syntaxReference.GetSyntax();
#pragma warning disable RS1030 // Closed source call-graph analysis requires the declaring tree semantic model.
                    var model = compilation.GetSemanticModel(declaration.SyntaxTree);
#pragma warning restore RS1030
                    foreach (var node in CallableBodyNodes(declaration))
                    {
                        SyntaxToken keyword = default;
                        string? display = null;
                        if (node is ThrowStatementSyntax throwStatement)
                        {
                            keyword = throwStatement.ThrowKeyword;
                            display = "throw";
                        }
                        else if (node is ThrowExpressionSyntax throwExpression)
                        {
                            keyword = throwExpression.ThrowKeyword;
                            display = "throw";
                        }
                        else if (node is TryStatementSyntax tryStatement)
                        {
                            keyword = tryStatement.TryKeyword;
                            display = "try/catch/finally";
                        }
                        if (display != null)
                        {
                            var location = keyword.GetLocation();
                            var key = (location.SourceTree!, location.SourceSpan.Start, location.SourceSpan.Length);
                            if (locations.Add(key)) result.Add(new ForbiddenExceptionUsage(location, display));
                        }

                        IMethodSymbol? called = null;
                        if (node is InvocationExpressionSyntax invocation)
                            called = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                        else if (node is ObjectCreationExpressionSyntax creation)
                            called = model.GetSymbolInfo(creation).Symbol as IMethodSymbol;
                        else if (node is ImplicitObjectCreationExpressionSyntax implicitCreation)
                            called = model.GetSymbolInfo(implicitCreation).Symbol as IMethodSymbol;
                        if (called != null && called.DeclaringSyntaxReferences.Length != 0)
                            pending.Enqueue(called.ReducedFrom ?? called.OriginalDefinition);
                    }
                }
            }
            result.Sort((left, right) =>
            {
                var comparison = string.CompareOrdinal(left.Location.SourceTree?.FilePath, right.Location.SourceTree?.FilePath);
                return comparison != 0 ? comparison : left.Location.SourceSpan.Start.CompareTo(right.Location.SourceSpan.Start);
            });
            return result;
        }

        private static IEnumerable<SyntaxNode> CallableBodyNodes(SyntaxNode declaration)
        {
            SyntaxNode? body = null;
            SyntaxNode? expression = null;
            if (declaration is MethodDeclarationSyntax method)
            {
                body = method.Body;
                expression = method.ExpressionBody?.Expression;
            }
            else if (declaration is LocalFunctionStatementSyntax local)
            {
                body = local.Body;
                expression = local.ExpressionBody?.Expression;
            }
            else if (declaration is ConstructorDeclarationSyntax constructor)
            {
                body = constructor.Body;
                expression = constructor.ExpressionBody?.Expression;
            }
            else if (declaration is OperatorDeclarationSyntax @operator)
            {
                body = @operator.Body;
                expression = @operator.ExpressionBody?.Expression;
            }
            else if (declaration is ConversionOperatorDeclarationSyntax conversion)
            {
                body = conversion.Body;
                expression = conversion.ExpressionBody?.Expression;
            }
            else if (declaration is AccessorDeclarationSyntax accessor)
            {
                body = accessor.Body;
                expression = accessor.ExpressionBody?.Expression;
            }
            else if (declaration is PropertyDeclarationSyntax property)
            {
                expression = property.ExpressionBody?.Expression;
            }
            else if (declaration is IndexerDeclarationSyntax indexer)
            {
                expression = indexer.ExpressionBody?.Expression;
            }
            if (declaration is ConstructorDeclarationSyntax withInitializer
                && withInitializer.Initializer != null)
                foreach (var node in withInitializer.Initializer.DescendantNodesAndSelf(ShouldDescendIntoCallable)) yield return node;
            if (body != null)
                foreach (var node in body.DescendantNodesAndSelf(ShouldDescendIntoCallable)) yield return node;
            if (expression != null)
                foreach (var node in expression.DescendantNodesAndSelf(ShouldDescendIntoCallable)) yield return node;
        }

        private static bool ShouldDescendIntoCallable(SyntaxNode node)
            => !(node is AnonymousFunctionExpressionSyntax) && !(node is LocalFunctionStatementSyntax);

        private static bool IsCallbackRoot(IMethodSymbol method)
        {
            if (!HasAttribute(method.ContainingType, NodeAttribute)) return false;
            switch (method.Name)
            {
                case "Enter": case "Tick": case "Abort": case "Exit": case "Evaluate": return true;
                default: return false;
            }
        }

        private static string SymbolOrder(IMethodSymbol method)
        {
            var location = method.Locations.FirstOrDefault(value => value.IsInSource);
            return (location?.SourceTree?.FilePath ?? string.Empty) + "\0"
                + (location?.SourceSpan.Start ?? int.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool ValidBinding(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol called, IMethodSymbol callback, string[] expectedAttributes, int argumentIndex)
        {
            if (invocation.ArgumentList.Arguments.Count <= argumentIndex || callback.Parameters.Length == 0) return false;
            var expression = invocation.ArgumentList.Arguments[argumentIndex].Expression;
            var member = expression as MemberAccessExpressionSyntax;
            var field = model.GetSymbolInfo(expression).Symbol as IFieldSymbol;
            var receiver = member == null ? null : model.GetSymbolInfo(member.Expression).Symbol as IParameterSymbol;
            var configParameter = callback.Parameters[0];
            if (field == null || receiver == null || !SymbolEqualityComparer.Default.Equals(receiver, configParameter)
                || !SymbolEqualityComparer.Default.Equals(field.ContainingType, configParameter.Type)) return false;
            var attribute = field.GetAttributes().FirstOrDefault(value => value.AttributeClass != null && expectedAttributes.Contains(value.AttributeClass.ToDisplayString(), StringComparer.Ordinal));
            return attribute != null && called.Parameters.Length > argumentIndex
                && SymbolEqualityComparer.Default.Equals(called.Parameters[argumentIndex].Type, field.Type)
                && PhaseAllows(callback, called.Name) && BindingKindAllows(attribute, called.Name);
        }

        internal static bool ValidUnresolvedBinding(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol callback, string operation, string[] expectedAttributes, int argumentIndex)
        {
            if (invocation.ArgumentList.Arguments.Count <= argumentIndex || callback.Parameters.Length == 0) return false;
            var expression = invocation.ArgumentList.Arguments[argumentIndex].Expression;
            var member = expression as MemberAccessExpressionSyntax;
            var field = model.GetSymbolInfo(expression).Symbol as IFieldSymbol;
            var receiver = member == null ? null : model.GetSymbolInfo(member.Expression).Symbol as IParameterSymbol;
            var configParameter = callback.Parameters[0];
            if (field == null || receiver == null || !SymbolEqualityComparer.Default.Equals(receiver, configParameter)
                || !SymbolEqualityComparer.Default.Equals(field.ContainingType, configParameter.Type)) return false;
            var attribute = field.GetAttributes().FirstOrDefault(value => value.AttributeClass != null && expectedAttributes.Contains(value.AttributeClass.ToDisplayString(), StringComparer.Ordinal));
            return attribute != null && PhaseAllows(callback, operation) && BindingKindAllows(attribute, operation);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (!(context.SemanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol called)) return;
            var callback = CallbackContaining(invocation, context.SemanticModel);
            if (callback == null) return;

            var contextName = called.ContainingType?.ToDisplayString();
            if (called.ContainingType?.Name == "BurstAccess")
            {
                var generatedExpected = BindingAttribute(called.Name);
                if (generatedExpected != null) AnalyzeBinding(context, invocation, called, callback, generatedExpected, 1);
                return;
            }
            if (contextName != null && contextName.StartsWith("AIBT.Burst.Burst", StringComparison.Ordinal)
                && contextName.EndsWith("Context", StringComparison.Ordinal))
            {
                if (called.Name == "TryNextUInt32" || called.Name == "TryNextFloat32")
                {
                    if (!HasAttribute(callback.ContainingType, RandomAttribute))
                        Report(context, AbiDiagnostics.WrongAccess, invocation, called.Name, "random-stream-marker");
                    return;
                }
                if (called.Name.StartsWith("TryBegin", StringComparison.Ordinal))
                    Report(context, AbiDiagnostics.UndeclaredAccess, invocation, called.Name);
            }
        }

        private static void AnalyzeBinding(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation,
            IMethodSymbol called, IMethodSymbol callback, string[] expectedAttributes, int argumentIndex)
        {
            if (invocation.ArgumentList.Arguments.Count <= argumentIndex || callback.Parameters.Length == 0)
            {
                Report(context, AbiDiagnostics.UndeclaredAccess, invocation, called.Name);
                return;
            }
            var expression = invocation.ArgumentList.Arguments[argumentIndex].Expression;
            var member = expression as MemberAccessExpressionSyntax;
            var field = context.SemanticModel.GetSymbolInfo(expression).Symbol as IFieldSymbol;
            var receiver = member == null ? null : context.SemanticModel.GetSymbolInfo(member.Expression).Symbol as IParameterSymbol;
            var configParameter = callback.Parameters[0];
            if (field == null || receiver == null || !SymbolEqualityComparer.Default.Equals(receiver, configParameter)
                || !SymbolEqualityComparer.Default.Equals(field.ContainingType, configParameter.Type))
            {
                Report(context, AbiDiagnostics.UndeclaredAccess, invocation, called.Name);
                return;
            }
            var attribute = field.GetAttributes().FirstOrDefault(value => value.AttributeClass != null && expectedAttributes.Contains(value.AttributeClass.ToDisplayString(), StringComparer.Ordinal));
            var compatibleHandle = called.Parameters.Length > argumentIndex && SymbolEqualityComparer.Default.Equals(called.Parameters[argumentIndex].Type, field.Type);
            if (attribute == null || !compatibleHandle
                || !PhaseAllows(callback, called.Name) || !BindingKindAllows(attribute, called.Name))
                Report(context, AbiDiagnostics.WrongAccess, invocation, called.Name, field.Name);
        }

        private static bool PhaseAllows(IMethodSymbol callback, string operation)
        {
            switch (callback.Name)
            {
                case "Enter": case "Tick": return operation != "TryCancel";
                case "Abort": return operation == "TryCancel";
                case "Evaluate": return operation == "TryRead" || operation == "TryReadSnapshot";
                default: return false;
            }
        }

        private static bool BindingKindAllows(AttributeData attribute, string operation)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name == "AIBT.Burst.AibtBlackboardBindingAttribute" && attribute.ConstructorArguments.Length > 1 && attribute.ConstructorArguments[1].Value != null)
            {
                var access = Convert.ToInt32(attribute.ConstructorArguments[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var scope = attribute.ConstructorArguments.Length > 2 && attribute.ConstructorArguments[2].Value != null
                    ? Convert.ToInt32(attribute.ConstructorArguments[2].Value, System.Globalization.CultureInfo.InvariantCulture) : -1;
                if (scope != 1 && scope != 2 && (scope != 3 || access != 0))
                    return false;
                if (operation == "TryRead") return access == 0 || access == 2;
                if (operation == "TryWrite") return access == 1 || access == 2;
            }
            if (name == "AIBT.Burst.AibtCommandBindingAttribute") return operation == "TryEmit";
            if (name == "AIBT.Burst.AibtAsyncOperationBindingAttribute") return operation == "TryStart" || operation == "TryCancel";
            return operation == "TryReadSnapshot" || operation == "TryConsume";
        }

        private static string[]? BindingAttribute(string operation)
        {
            switch (operation)
            {
                case "TryRead": return new[] { "AIBT.Burst.AibtBlackboardBindingAttribute" };
                case "TryWrite": return new[] { "AIBT.Burst.AibtBlackboardBindingAttribute" };
                case "TryReadSnapshot": return new[] { "AIBT.Burst.AibtSnapshotBindingAttribute" };
                case "TryEmit": return new[] { "AIBT.Burst.AibtCommandBindingAttribute" };
                case "TryStart": case "TryCancel": return new[] { "AIBT.Burst.AibtAsyncOperationBindingAttribute" };
                case "TryConsume": return new[] { "AIBT.Burst.AibtCompletionBindingAttribute" };
                default: return null;
            }
        }

        private static void AnalyzeForbiddenSymbol(SyntaxNodeAnalysisContext context)
        {
            if (context.Node is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "var") return;
            var callback = CallbackContaining(context.Node, context.SemanticModel);
            if (callback == null) return;
            var symbol = ResolvedSymbol(context.SemanticModel, context.Node);
            var outermost = OutermostForbiddenSyntax(context.Node);
            if (outermost is ObjectCreationExpressionSyntax || outermost is ImplicitObjectCreationExpressionSyntax) return;
            var reporter = outermost.DescendantNodesAndSelf().Where(IsForbiddenSymbolSyntax).OrderByDescending(node => node.Span.Length).ThenBy(node => node.SpanStart).FirstOrDefault();
            if (reporter != context.Node) return;
            if (TryForbiddenApi(symbol, context.SemanticModel.GetTypeInfo(context.Node).Type, out var display))
                Report(context, AbiDiagnostics.Forbidden, outermost, display);
        }

        private static void AnalyzeAllocation(SyntaxNodeAnalysisContext context)
        {
            if (CallbackContaining(context.Node, context.SemanticModel) == null) return;
            var type = context.SemanticModel.GetTypeInfo(context.Node).Type;
            if (TryForbiddenApi(ResolvedSymbol(context.SemanticModel, context.Node), type, out var display))
            {
                Report(context, AbiDiagnostics.Forbidden, context.Node, display);
                return;
            }
            if ((context.Node.IsKind(SyntaxKind.ObjectCreationExpression) || context.Node.IsKind(SyntaxKind.ImplicitObjectCreationExpression))
                && type != null && type.IsValueType && type.IsUnmanagedType)
                return;
            Report(context, AbiDiagnostics.Forbidden, context.Node, type?.ToDisplayString() ?? context.Node.Kind().ToString());
        }

        private static bool TryForbiddenApiUsage(
            SemanticModel model,
            SyntaxNode node,
            out SyntaxNode usageNode,
            out string display)
        {
            usageNode = node;
            display = string.Empty;
            if (!(node is InvocationExpressionSyntax)
                && !(node is ObjectCreationExpressionSyntax)
                && !(node is ImplicitObjectCreationExpressionSyntax)
                && !(node is MemberAccessExpressionSyntax)
                && !(node is IdentifierNameSyntax)
                && !(node is QualifiedNameSyntax)
                && !(node is TypeOfExpressionSyntax))
                return false;
            if (node is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "var") return false;
            var symbol = ResolvedSymbol(model, node);
            if (!TryForbiddenApi(symbol, model.GetTypeInfo(node).Type, out display)) return false;
            usageNode = OutermostForbiddenSyntax(node);
            return true;
        }

        private static ISymbol? ResolvedSymbol(SemanticModel model, SyntaxNode node)
        {
            var symbol = model.GetSymbolInfo(node).Symbol;
            if (symbol == null && node is TypeOfExpressionSyntax typeOf)
                symbol = model.GetTypeInfo(typeOf.Type).Type;
            return symbol;
        }

        private static bool TryForbiddenApi(ISymbol? symbol, ITypeSymbol? fallbackType, out string display)
        {
            var type = SymbolType(symbol) ?? fallbackType;
            display = symbol?.ToDisplayString() ?? type?.ToDisplayString() ?? string.Empty;
            var symbolNamespace = symbol?.ContainingNamespace?.ToDisplayString();
            if (display == "string" || display == "System.String" || ForbiddenNamespace(symbolNamespace)) return true;
            if (IsForbiddenType(type)) return true;
            var containingType = symbol?.ContainingType;
            return containingType != null
                && !SymbolEqualityComparer.Default.Equals(type, containingType)
                && IsForbiddenType(containingType);
        }

        private static bool IsForbiddenType(ITypeSymbol? type)
        {
            if (type == null) return false;
            if (type.SpecialType == SpecialType.System_String) return true;
            if (ForbiddenNamespace(type.ContainingNamespace?.ToDisplayString())) return true;

            for (var current = type as INamedTypeSymbol; current != null; current = current.ContainingType)
            {
                var ns = current.ContainingNamespace?.ToDisplayString();
                if (NamespaceIs(ns, "Unity.Collections") && current.Name.StartsWith("Native", StringComparison.Ordinal))
                    return true;
                if (NamespaceIs(ns, "Unity.Burst") && current.Name.StartsWith("SharedStatic", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static ITypeSymbol? SymbolType(ISymbol? symbol)
        {
            switch (symbol)
            {
                case ITypeSymbol type: return type;
                case ILocalSymbol local: return local.Type;
                case IParameterSymbol parameter: return parameter.Type;
                case IFieldSymbol field: return field.Type;
                case IPropertySymbol property: return property.Type;
                case IEventSymbol eventSymbol: return eventSymbol.Type;
                case IMethodSymbol method: return method.ContainingType;
                default: return symbol?.ContainingType;
            }
        }

        private static bool ForbiddenNamespace(string? value)
            => NamespaceIs(value, "UnityEngine")
                || NamespaceIs(value, "Unity.Jobs")
                || NamespaceIs(value, "Unity.Collections.LowLevel.Unsafe")
                || NamespaceIs(value, "System.Reflection")
                || NamespaceIs(value, "System.Threading.Tasks");

        private static bool NamespaceIs(string? value, string expected)
            => value == expected || value != null && value.StartsWith(expected + ".", StringComparison.Ordinal);

        private static IMethodSymbol? CallbackContaining(SyntaxNode node, SemanticModel model)
        {
            var declaration = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (declaration == null) return null;
            var method = model.GetDeclaredSymbol(declaration) as IMethodSymbol;
            return method != null && HasAttribute(method.ContainingType, NodeAttribute) ? method : null;
        }

        private static bool HasAttribute(ISymbol symbol, string name)
            => symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == name);

        private static void Report(SyntaxNodeAnalysisContext context, DiagnosticDescriptor descriptor, SyntaxNode node, params object[] args)
            => context.ReportDiagnostic(Diagnostic.Create(descriptor, node.GetLocation(), args));

        private static SyntaxNode OutermostForbiddenSyntax(SyntaxNode node)
        {
            var current = node;
            while (current.Parent is ExpressionSyntax || current.Parent is QualifiedNameSyntax) current = current.Parent;
            return current;
        }
        private static bool IsForbiddenSymbolSyntax(SyntaxNode node) => node.IsKind(SyntaxKind.SimpleMemberAccessExpression) || node.IsKind(SyntaxKind.IdentifierName)
            || node.IsKind(SyntaxKind.QualifiedName) || node.IsKind(SyntaxKind.TypeOfExpression);

        private readonly struct ForbiddenExceptionUsage
        {
            internal ForbiddenExceptionUsage(Location location, string display)
            {
                Location = location;
                Display = display;
            }

            internal Location Location { get; }
            internal string Display { get; }
        }
    }
}
