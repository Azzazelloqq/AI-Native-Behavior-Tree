using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace AIBT.CodeGen
{
    /// <summary>One attribute argument, read from source or synthesized from a short form.</summary>
    internal readonly struct Arg
    {
        private Arg(object? value, TypedConstantKind kind, ImmutableArray<Arg> values, bool isNull)
        { Value = value; Kind = kind; Values = values; IsNull = isNull; }

        internal object? Value { get; }
        internal TypedConstantKind Kind { get; }
        internal ImmutableArray<Arg> Values { get; }
        internal bool IsNull { get; }

        internal static Arg From(TypedConstant constant)
            => constant.Kind == TypedConstantKind.Array
                ? new Arg(null, constant.Kind, constant.IsNull ? ImmutableArray<Arg>.Empty : constant.Values.Select(From).ToImmutableArray(), constant.IsNull)
                : new Arg(constant.Value, constant.Kind, ImmutableArray<Arg>.Empty, constant.IsNull);

        internal static Arg Of(object? value, TypedConstantKind kind)
            => new Arg(value, kind, ImmutableArray<Arg>.Empty, value == null);
    }

    /// <summary>
    /// An AIBT attribute as the generator and analyzer see it: either the attribute written in source or
    /// the long <c>AIBT.Burst</c> attribute a short <c>AIBT.Native</c> attribute stands for.
    /// </summary>
    internal sealed class Attr
    {
        internal Attr(INamedTypeSymbol? attributeClass, ImmutableArray<Arg> arguments, SyntaxReference? syntax, bool isShortForm)
        { AttributeClass = attributeClass; ConstructorArguments = arguments; ApplicationSyntaxReference = syntax; IsShortForm = isShortForm; }

        internal INamedTypeSymbol? AttributeClass { get; }
        internal ImmutableArray<Arg> ConstructorArguments { get; }
        internal SyntaxReference? ApplicationSyntaxReference { get; }
        /// <summary>Synthesized from a short form: argument positions do not exist in source.</summary>
        internal bool IsShortForm { get; }
    }

    /// <summary>
    /// P9-005 (ADR-P9-005): expands AIBT.Native short declarations into the long AIBT.Burst attributes before
    /// any validation or emission, so both spellings share one code path and one output.
    /// </summary>
    internal static class ShortFormNormalization
    {
        internal const string ShortNamespace = "AIBT.Native";
        private const string Action = ShortNamespace + ".AibtActionAttribute";
        private const string Condition = ShortNamespace + ".AibtConditionAttribute";
        private const string Param = ShortNamespace + ".AibtParamAttribute";
        private const string Memory = ShortNamespace + ".AibtMemoryAttribute";
        private const string Key = ShortNamespace + ".AibtKeyAttribute";
        private const string Command = ShortNamespace + ".AibtCommandAttribute";

        private sealed class Holder { internal ImmutableArray<Attr> Value; }
        private static readonly ConditionalWeakTable<ISymbol, Holder> Cache = new ConditionalWeakTable<ISymbol, Holder>();

        /// <summary>The symbol's attributes with every short form replaced by its long equivalent(s).</summary>
        internal static ImmutableArray<Attr> AibtAttributes(this ISymbol symbol)
            => Cache.GetValue(symbol, key => new Holder { Value = Normalize(key) }).Value;

        private static ImmutableArray<Attr> Normalize(ISymbol symbol)
        {
            var result = ImmutableArray.CreateBuilder<Attr>();
            foreach (var attribute in symbol.GetAttributes())
            {
                var name = attribute.AttributeClass?.ToDisplayString();
                var syntax = attribute.ApplicationSyntaxReference;
                var assembly = attribute.AttributeClass?.ContainingAssembly;
                switch (name)
                {
                    case Action:
                    case Condition:
                        if (symbol is INamedTypeSymbol node && assembly != null) result.Add(NodeAttribute(node, attribute, name == Action, assembly));
                        continue;
                    case Param:
                        if (symbol is IFieldSymbol parameter && assembly != null)
                            result.Add(Field(assembly, "AIBT.Burst.AibtConfigFieldAttribute", FirstString(attribute), parameter.Type, syntax));
                        continue;
                    case Memory:
                        if (symbol is IFieldSymbol memory && assembly != null)
                            result.Add(Field(assembly, "AIBT.Burst.AibtMemoryFieldAttribute", FirstString(attribute), memory.Type, syntax));
                        continue;
                    case Key:
                        if (symbol is IFieldSymbol keyField && assembly != null)
                        {
                            var id = FirstString(attribute);
                            result.Add(Field(assembly, "AIBT.Burst.AibtConfigFieldAttribute", id, keyField.Type, syntax));
                            result.Add(BlackboardBinding(assembly, id, keyField.Type, Named(attribute, "Scope", (byte)1), syntax));
                        }
                        continue;
                    case Command:
                        if (symbol is IFieldSymbol commandField && assembly != null)
                        {
                            var id = FirstString(attribute);
                            result.Add(Field(assembly, "AIBT.Burst.AibtConfigFieldAttribute", id, commandField.Type, syntax));
                            Infer(TypeArgument(commandField.Type), out var payloadId, out var payloadVersion);
                            result.Add(new Attr(assembly.GetTypeByMetadataName("AIBT.Burst.AibtCommandBindingAttribute"),
                                ImmutableArray.Create(Arg.Of(id, TypedConstantKind.Primitive), Arg.Of(payloadId, TypedConstantKind.Primitive), Arg.Of(payloadVersion, TypedConstantKind.Primitive)), syntax, true));
                        }
                        continue;
                    default:
                        result.Add(new Attr(attribute.AttributeClass, attribute.ConstructorArguments.Select(Arg.From).ToImmutableArray(), syntax, false));
                        continue;
                }
            }
            return result.ToImmutable();
        }

        private static Attr NodeAttribute(INamedTypeSymbol node, AttributeData attribute, bool action, IAssemblySymbol assembly)
        {
            // Configuration and memory types come from Tick(in TConfig, ref TMemory, ref BurstTickContext).
            var tick = node.GetMembers("Tick").OfType<IMethodSymbol>().FirstOrDefault(method => method.Parameters.Length >= 2);
            var arguments = ImmutableArray.Create(
                Arg.Of(FirstString(attribute), TypedConstantKind.Primitive),
                Arg.Of(Named(attribute, "Version", 1u), TypedConstantKind.Primitive),
                Arg.Of(action ? (byte)1 : (byte)0, TypedConstantKind.Enum),
                Arg.Of(tick?.Parameters[0].Type as INamedTypeSymbol, TypedConstantKind.Type),
                Arg.Of(tick?.Parameters[1].Type as INamedTypeSymbol, TypedConstantKind.Type),
                Arg.Of(Named(attribute, "MemoryLifetime", (byte)0), TypedConstantKind.Enum),
                Arg.Of(Named(attribute, "Deterministic", true), TypedConstantKind.Primitive),
                Arg.Of(Named(attribute, "Cancellation", action ? (byte)1 : (byte)0), TypedConstantKind.Enum),
                Arg.Of(Named(attribute, "Cost", action ? (byte)1 : (byte)0), TypedConstantKind.Enum),
                Arg.Of(Named(attribute, "Statuses", action ? (byte)7 : (byte)3), TypedConstantKind.Enum));
            return new Attr(assembly.GetTypeByMetadataName("AIBT.Burst.AibtBurstNodeAttribute"), arguments, attribute.ApplicationSyntaxReference, true);
        }

        private static Attr Field(IAssemblySymbol assembly, string longName, string id, ITypeSymbol type, SyntaxReference? syntax)
        {
            Infer(type, out var typeId, out var version);
            return new Attr(assembly.GetTypeByMetadataName(longName),
                ImmutableArray.Create(Arg.Of(id, TypedConstantKind.Primitive), Arg.Of(typeId, TypedConstantKind.Primitive), Arg.Of(version, TypedConstantKind.Primitive)), syntax, true);
        }

        private static Attr BlackboardBinding(IAssemblySymbol assembly, string id, ITypeSymbol handle, byte scope, SyntaxReference? syntax)
        {
            var generic = (handle as INamedTypeSymbol)?.IsGenericType == true ? ((INamedTypeSymbol)handle).ConstructedFrom.ToDisplayString() : string.Empty;
            // An unknown handle yields an impossible access so the usual binding diagnostic is reported.
            var access = generic == "AIBT.Burst.BlackboardReadHandle<T>" ? (byte)0 : generic == "AIBT.Burst.BlackboardWriteHandle<T>" ? (byte)1
                : generic == "AIBT.Burst.BlackboardReadWriteHandle<T>" ? (byte)2 : byte.MaxValue;
            Infer(TypeArgument(handle), out var valueId, out var valueVersion);
            return new Attr(assembly.GetTypeByMetadataName("AIBT.Burst.AibtBlackboardBindingAttribute"),
                ImmutableArray.Create(Arg.Of(id, TypedConstantKind.Primitive), Arg.Of(access, TypedConstantKind.Enum), Arg.Of(scope, TypedConstantKind.Enum),
                    Arg.Of(valueId, TypedConstantKind.Primitive), Arg.Of(valueVersion, TypedConstantKind.Primitive)), syntax, true);
        }

        private static ITypeSymbol? TypeArgument(ITypeSymbol type)
            => type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1 ? named.TypeArguments[0] : null;

        /// <summary>The canonical value type identity of <paramref name="type"/>; an empty id when there is none.</summary>
        internal static void Infer(ITypeSymbol? type, out string id, out uint version)
        {
            id = string.Empty; version = 1;
            if (type == null) return;
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean: id = "Bool"; return; case SpecialType.System_SByte: id = "Int8"; return;
                case SpecialType.System_Byte: id = "UInt8"; return; case SpecialType.System_Int16: id = "Int16"; return;
                case SpecialType.System_UInt16: id = "UInt16"; return; case SpecialType.System_Int32: id = "Int32"; return;
                case SpecialType.System_UInt32: id = "UInt32"; return; case SpecialType.System_Int64: id = "Int64"; return;
                case SpecialType.System_UInt64: id = "UInt64"; return; case SpecialType.System_Single: id = "Float32"; return;
                case SpecialType.System_Double: id = "Float64"; return;
            }
            if (type is INamedTypeSymbol named)
            {
                if (named.IsGenericType && named.ContainingNamespace?.ToDisplayString() == "AIBT.Burst" && named.Name.EndsWith("Handle", StringComparison.Ordinal))
                { id = "GeneratedHandle"; return; }
                var registered = named.GetAttributes().FirstOrDefault(value => value.AttributeClass?.ToDisplayString() == "AIBT.Burst.AibtBurstValueAttribute");
                if (registered != null && registered.ConstructorArguments.Length >= 2 && registered.ConstructorArguments[0].Value is string registeredId
                    && registered.ConstructorArguments[1].Value is uint registeredVersion)
                { id = registeredId; version = registeredVersion; return; }
            }
            switch (type.ToDisplayString())
            {
                case "AIBT.Float2Value": id = "Float2"; return;
                case "AIBT.Float3Value": id = "Float3"; return;
                case "AIBT.QuaternionValue": id = "Quaternion"; return;
                case "Unity.Collections.FixedString32Bytes": id = "FixedString32"; return;
                case "Unity.Collections.FixedString64Bytes": id = "FixedString64"; return;
                case "Unity.Collections.FixedString128Bytes": id = "FixedString128"; return;
                case "Unity.Collections.FixedString512Bytes": id = "FixedString512"; return;
                case "AIBT.AgentId": id = "AgentId"; return;
                case "AIBT.EntityId": id = "EntityId"; return;
                case "AIBT.OperationId": id = "OperationId"; return;
                case "AIBT.AssetId": id = "AssetId"; return;
            }
        }

        private static string FirstString(AttributeData attribute)
            => attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string ?? string.Empty : string.Empty;

        private static T Named<T>(AttributeData attribute, string name, T fallback)
        {
            foreach (var pair in attribute.NamedArguments)
                if (pair.Key == name && pair.Value.Value is T value) return value;
            return fallback;
        }
    }
}
