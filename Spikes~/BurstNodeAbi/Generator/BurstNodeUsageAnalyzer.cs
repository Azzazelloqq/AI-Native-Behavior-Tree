using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AIBT.BurstNodeAbi.Feasibility
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
        }

        internal static bool HasInvalidUsage(Compilation compilation)
        {
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
                        else if (node is InvocationExpressionSyntax unresolved && unresolved.Expression.ToString().Contains(".BurstAccess.", StringComparison.Ordinal))
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
                        var symbol = model.GetSymbolInfo(node).Symbol;
                        if (symbol == null && node is TypeOfExpressionSyntax typeOf) symbol = model.GetTypeInfo(typeOf.Type).Type;
                        var symbolType = symbol as ITypeSymbol ?? symbol?.ContainingType;
                        var ns = symbolType?.ContainingNamespace?.ToDisplayString() ?? symbol?.ContainingNamespace?.ToDisplayString();
                        var display = symbol?.ToDisplayString() ?? string.Empty;
                        if (ns == "UnityEngine" || (ns != null && ns.StartsWith("UnityEngine.", StringComparison.Ordinal))
                            || display.StartsWith("System.Reflection.", StringComparison.Ordinal) || display.StartsWith("System.Threading.Tasks.", StringComparison.Ordinal)
                            || display == "string" || display == "System.String") return true;
                    }
                }
            }
            return false;
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
            if (attribute == null || !compatibleHandle || !PhaseAllows(callback, called.Name) || !BindingKindAllows(attribute, called.Name))
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
                if (scope == 3 && access != 0)
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
            var symbol = context.SemanticModel.GetSymbolInfo(context.Node).Symbol;
            if (symbol == null && context.Node is TypeOfExpressionSyntax typeOf) symbol = context.SemanticModel.GetTypeInfo(typeOf.Type).Type;
            if (symbol == null) return;
            var outermost = OutermostForbiddenSyntax(context.Node);
            if (outermost is ObjectCreationExpressionSyntax || outermost is ImplicitObjectCreationExpressionSyntax) return;
            var reporter = outermost.DescendantNodesAndSelf().Where(IsForbiddenSymbolSyntax).OrderByDescending(node => node.Span.Length).ThenBy(node => node.SpanStart).FirstOrDefault();
            if (reporter != context.Node) return;
            var type = symbol as ITypeSymbol ?? symbol.ContainingType;
            var ns = type?.ContainingNamespace?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString();
            var display = symbol.ToDisplayString();
            if (ns == "UnityEngine" || (ns != null && ns.StartsWith("UnityEngine.", StringComparison.Ordinal))
                || display.StartsWith("System.Reflection.", StringComparison.Ordinal)
                || display.StartsWith("System.Threading.Tasks.", StringComparison.Ordinal)
                || display == "string" || display == "System.String")
                Report(context, AbiDiagnostics.Forbidden, outermost, display);
        }

        private static void AnalyzeAllocation(SyntaxNodeAnalysisContext context)
        {
            if (CallbackContaining(context.Node, context.SemanticModel) == null) return;
            var type = context.SemanticModel.GetTypeInfo(context.Node).Type;
            if ((context.Node.IsKind(SyntaxKind.ObjectCreationExpression) || context.Node.IsKind(SyntaxKind.ImplicitObjectCreationExpression))
                && type != null && type.IsValueType && type.IsUnmanagedType)
                return;
            Report(context, AbiDiagnostics.Forbidden, context.Node, type?.ToDisplayString() ?? context.Node.Kind().ToString());
        }

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
    }
}
