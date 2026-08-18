using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIBT.Authoring
{
    public static class GeneratedShardMetadataMaterializer
    {
        public static IReadOnlyList<GeneratedNodeDescriptor> Materialize(
            string descriptorJson, string descriptorHash, string registryJson, string registryHash)
            => MaterializeArtifact(descriptorJson, descriptorHash, registryJson, registryHash).Nodes;

        public static GeneratedShardMetadataArtifact MaterializeArtifact(
            string descriptorJson, string descriptorHash, string registryJson, string registryHash)
            => MaterializeArtifact(null, 0u, descriptorJson, descriptorHash, registryJson, registryHash);

        public static GeneratedShardMetadataArtifact MaterializeArtifact(
            string shardId, uint shardVersion,
            string descriptorJson, string descriptorHash, string registryJson, string registryHash)
        {
            RequireHash(descriptorJson, descriptorHash, nameof(descriptorHash));
            RequireHash(registryJson, registryHash, nameof(registryHash));
            var descriptorRoot = JObject.Parse(descriptorJson);
            var registryRoot = JObject.Parse(registryJson);
            if (!string.Equals(descriptorRoot.ToString(Formatting.None), descriptorJson, StringComparison.Ordinal))
                throw new ArgumentException("Generated descriptor JSON is not in the exact canonical compact form.", nameof(descriptorJson));
            RequireMembers(descriptorRoot, "descriptor", new[] { "abiVersion", "registeredTypes", "nodes" });
            RequireMembers(registryRoot, "registry", new[] { "format", "formatVersion", "manifests" });
            if ((uint)descriptorRoot["abiVersion"] != 2 || (string)registryRoot["format"] != "aibt-node-registry" || (uint)registryRoot["formatVersion"] != 1)
                throw new ArgumentException("Unsupported generated shard metadata version.");
            var registeredTypes = ReadRegisteredTypes((JArray)descriptorRoot["registeredTypes"]);
            var manifests = ((JArray)registryRoot["manifests"]).Cast<JObject>().ToDictionary(
                value => Identity((string)value["typeId"], (uint)value["version"]), StringComparer.Ordinal);
            var result = new List<GeneratedNodeDescriptor>();
            foreach (var node in ((JArray)descriptorRoot["nodes"]).Cast<JObject>())
            {
                RequireMembers(node, "node descriptor", new[]
                {
                    "typeId", "version", "callbackCapabilities", "hasRandom",
                    "configurationHash", "memoryHash", "accessHash",
                    "configuration", "memory", "bindings",
                });
                var identity = Identity((string)node["typeId"], (uint)node["version"]);
                if (!manifests.TryGetValue(identity, out var manifestJson)) throw new ArgumentException("Generated descriptor has no exact P1 manifest projection.");
                var configurationJson = (JObject)node["configuration"];
                var memoryJson = (JObject)node["memory"];
                var configuration = ReadFields(configurationJson, registeredTypes);
                var memory = ReadFields(memoryJson, registeredTypes);
                var bindings = ReadBindings((JArray)node["bindings"], registeredTypes);
                var manifest = ReadManifest(manifestJson, configuration, memory);
                var callbackCapabilities = RequireByte(node["callbackCapabilities"], "callback capabilities");
                var descriptor = new GeneratedNodeDescriptor(
                    manifest, configuration, memory, bindings,
                    callbackCapabilities, (bool)node["hasRandom"]);
                ValidateLayout(configurationJson, descriptor.Configuration, descriptor.Manifest.Configuration.Size, descriptor.Manifest.Configuration.Alignment);
                ValidateLayout(memoryJson, descriptor.Memory, descriptor.Manifest.Memory.Size, descriptor.Manifest.Memory.Alignment);
                ValidateBindings((JArray)node["bindings"], descriptor.Bindings);
                RequireDeclaredHash((string)node["configurationHash"], descriptor.ConfigurationLayoutHash, "configurationHash");
                RequireDeclaredHash((string)node["memoryHash"], descriptor.MemoryLayoutHash, "memoryHash");
                RequireDeclaredHash((string)node["accessHash"], descriptor.AccessLayoutHash, "accessHash");
                result.Add(descriptor);
            }
            result.Sort((left, right) =>
            {
                var comparison = Utf8OrdinalComparer.Instance.Compare(left.Manifest.TypeId, right.Manifest.TypeId);
                return comparison != 0 ? comparison : left.Manifest.Version.CompareTo(right.Manifest.Version);
            });
            if (result.Count != manifests.Count) throw new ArgumentException("Generated descriptor and P1 manifest sets differ.");
            var rebuilt = new NodeRegistryBuilder();
            for (var index = 0; index < result.Count; index++) rebuilt.AddUserExtension(result[index].Manifest);
            var rebuiltResult = rebuilt.Build();
            if (!rebuiltResult.Success) throw new ArgumentException("Generated registry projection cannot be rebuilt canonically.");
            var rebuiltRegistry = NodeManifestCanonicalJson.SerializeRegistry(rebuiltResult.Registry.ToArray());
            if (!string.Equals(rebuiltRegistry, registryJson, StringComparison.Ordinal)
                || !string.Equals(StableHash.Sha256Hex(rebuiltRegistry), registryHash, StringComparison.Ordinal))
                throw new ArgumentException("Generated registry JSON/hash is not the exact canonical manifest projection.");
            return new GeneratedShardMetadataArtifact(
                shardId, shardVersion, result.AsReadOnly(), registeredTypes);
        }

        private static RegisteredBlackboardTypeCatalog ReadRegisteredTypes(JArray values)
        {
            if (values == null) throw new ArgumentException("Generated registered type catalog is required.");
            var raw = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var value in values.Cast<JObject>())
            {
                RequireMembers(value, "registered type", new[] { "id", "numericId", "version", "schemaId", "numericSchemaId", "schemaHash", "equalityContractId", "size", "alignment", "fields" });
                var typeId = RegisteredId((string)value["id"], "type");
                var schemaId = RegisteredId((string)value["schemaId"], "schema");
                if (ParseHex((string)value["numericId"]) != StableHash.Fnv1A64(typeId)
                    || ParseHex((string)value["numericSchemaId"]) != StableHash.Fnv1A64(schemaId))
                    throw new ArgumentException("Registered catalog numeric identities differ from canonical UTF-8 IDs.");
                var identity = Identity(typeId, (uint)value["version"]);
                if (raw.ContainsKey(identity)) throw new ArgumentException("Registered catalog identities must be unique.");
                raw.Add(identity, value);
            }
            var built = new Dictionary<string, RegisteredBlackboardTypeCatalogEntry>(StringComparer.Ordinal);
            var active = new HashSet<string>(StringComparer.Ordinal);
            Func<string, RegisteredBlackboardTypeCatalogEntry> build = null;
            build = identity =>
            {
                if (built.TryGetValue(identity, out var existing)) return existing;
                if (!raw.TryGetValue(identity, out var value)) throw new ArgumentException("Registered field references an absent catalog type.");
                if (!active.Add(identity)) throw new ArgumentException("Recursive registered value schemas are not supported by the fixed unmanaged codec.");
                var typeId = (string)value["id"]; var version = (uint)value["version"];
                var schemaId = (string)value["schemaId"]; var schemaHash = (string)value["schemaHash"];
                var equality = ParseHex((string)value["equalityContractId"]);
                var size = (uint)value["size"]; var alignment = RequireByte(value["alignment"], "registered alignment");
                var descriptor = new RegisteredUnmanagedTypeDescriptor(StableHash.Fnv1A64(typeId), version, checked((int)size), alignment,
                    equality, StableHash.Fnv1A64(schemaId));
                var fields = new List<GeneratedStorageField>();
                foreach (var fieldValue in ((JArray)value["fields"]).Cast<JObject>())
                {
                    RequireMembers(fieldValue, "registered field", new[] { "id", "numericId", "type", "version", "schemaHash", "offset", "size", "alignment", "encoding" });
                    var fieldId = (string)fieldValue["id"];
                    if (ParseHex((string)fieldValue["numericId"]) != StableHash.Fnv1A64(fieldId))
                        throw new ArgumentException("Registered field numeric identity differs from its canonical ID.");
                    var fieldTypeId = (string)fieldValue["type"]; var fieldVersion = (uint)fieldValue["version"];
                    var encoding = FieldEncoding(fieldValue["encoding"]); var fieldSchemaHash = (string)fieldValue["schemaHash"];
                    RegisteredUnmanagedTypeDescriptor nested = default;
                    if (encoding == GeneratedFieldEncoding.Registered)
                    {
                        var nestedEntry = build(Identity(fieldTypeId, fieldVersion));
                        if (!string.Equals(fieldSchemaHash, nestedEntry.SchemaHash, StringComparison.Ordinal))
                            throw new ArgumentException("Registered nested field schema hash differs from its catalog entry.");
                        nested = nestedEntry.Descriptor;
                    }
                    var field = new GeneratedStorageField(fieldId, fieldTypeId, fieldVersion, (uint)fieldValue["size"],
                        RequireByte(fieldValue["alignment"], "registered field alignment"), encoding, fieldSchemaHash, null, nested);
                    field.Offset = (uint)fieldValue["offset"];
                    fields.Add(field);
                }
                var entry = new RegisteredBlackboardTypeCatalogEntry(typeId, version, schemaId, schemaHash, descriptor, fields);
                if (!string.Equals(ComputeRegisteredSchemaHash(entry), schemaHash, StringComparison.Ordinal))
                    throw new ArgumentException("Registered catalog schema hash differs from its canonical field stream.");
                active.Remove(identity); built.Add(identity, entry); return entry;
            };
            foreach (var identity in raw.Keys.OrderBy(value => value, StringComparer.Ordinal)) build(identity);
            return new RegisteredBlackboardTypeCatalog(built.Values);
        }

        private static string ComputeRegisteredSchemaHash(RegisteredBlackboardTypeCatalogEntry entry)
            => StableHash.Sha256Hex(GeneratedBlackboardContractBytesV2.RegisteredSchema(entry));

        private static List<GeneratedStorageField> ReadFields(JObject layout, RegisteredBlackboardTypeCatalog registeredTypes)
        {
            var result = new List<GeneratedStorageField>();
            foreach (var value in ((JArray)layout["fields"]).Cast<JObject>())
            {
                RequireMembers(value, "storage field", new[] { "id", "type", "version", "offset", "size", "alignment", "encoding", "bindingId", "schemaId", "schemaHash", "equalityContractId" });
                var encoding = FieldEncoding(value["encoding"]);
                var schemaHash = (string)value["schemaHash"];
                var schemaId = (string)value["schemaId"];
                var equality = ParseHex((string)value["equalityContractId"]);
                var typeId = (string)value["type"];
                var version = (uint)value["version"];
                var size = (uint)value["size"];
                var alignment = (byte)value["alignment"];
                var registered = ResolveRegisteredReference(registeredTypes, typeId, version, size, alignment,
                    encoding, schemaId, schemaHash, equality, "storage field");
                var field = new GeneratedStorageField((string)value["id"], typeId, version, size, alignment, encoding,
                    schemaHash, EmptyToNull((string)value["bindingId"]), registered);
                field.Offset = (uint)value["offset"];
                result.Add(field);
            }
            return result;
        }

        private static List<GeneratedBindingDescriptor> ReadBindings(JArray values, RegisteredBlackboardTypeCatalog registeredTypes)
        {
            var result = new List<GeneratedBindingDescriptor>();
            foreach (var value in values.Cast<JObject>())
            {
                RequireMembers(value, "binding", new[] { "id", "numericId", "kind", "scope", "phase", "ordinal", "types" });
                var types = new List<GeneratedTypeRecord>();
                foreach (var type in ((JArray)value["types"]).Cast<JObject>())
                {
                    RequireMembers(type, "binding type", new[] { "role", "id", "version", "size", "alignment", "encoding", "schemaId", "schemaHash", "equalityContractId" });
                    var typeId = (string)type["id"]; var version = (uint)type["version"];
                    var schemaId = (string)type["schemaId"]; var schemaHash = (string)type["schemaHash"];
                    var equality = ParseHex((string)type["equalityContractId"]);
                    var registered = ResolveRegisteredReference(registeredTypes, typeId, version,
                        (uint)type["size"], (byte)type["alignment"], FieldEncoding(type["encoding"]),
                        schemaId, schemaHash, equality, "binding type");
                    ValidateBindingTypeShape(type, typeId, version, registered);
                    types.Add(new GeneratedTypeRecord(TypeRole(type["role"]), typeId, version, schemaHash, registered));
                }
                result.Add(new GeneratedBindingDescriptor((string)value["id"], BindingKind(value["kind"]),
                    BindingScope(value["scope"]), Phase(value["phase"]), types));
            }
            return result;
        }

        private static RegisteredUnmanagedTypeDescriptor ResolveRegisteredReference(
            RegisteredBlackboardTypeCatalog registeredTypes,
            string typeId,
            uint version,
            uint size,
            byte alignment,
            GeneratedFieldEncoding encoding,
            string schemaId,
            string schemaHash,
            ulong equality,
            string label)
        {
            if (encoding != GeneratedFieldEncoding.Registered)
            {
                if (!string.IsNullOrEmpty(schemaId))
                    throw new ArgumentException("Generated " + label + " built-in metadata cannot name a registered schema.");
                return default;
            }
            RegisteredId(typeId, "type");
            RegisteredId(schemaId, "schema");
            if (!registeredTypes.TryGet(typeId, version, out var entry)
                || !string.Equals(schemaId, entry.CanonicalSchemaId, StringComparison.Ordinal)
                || !string.Equals(schemaHash, entry.SchemaHash, StringComparison.Ordinal)
                || equality != entry.Descriptor.EqualityContractId
                || size != entry.Descriptor.Size
                || alignment != entry.Descriptor.Alignment)
                throw new ArgumentException("Generated " + label + " registered metadata differs from its unique catalog entry.");
            return entry.Descriptor;
        }

        private static void ValidateLayout(JObject declared, IReadOnlyList<GeneratedStorageField> fields, uint size, byte alignment)
        {
            if ((uint)declared["size"] != size || (byte)declared["alignment"] != alignment)
                throw new ArgumentException("Generated layout size/alignment differs from its canonical materialization.");
            var values = ((JArray)declared["fields"]).Cast<JObject>().ToDictionary(value => (string)value["id"], StringComparer.Ordinal);
            if (values.Count != fields.Count) throw new ArgumentException("Generated layout field sets differ.");
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                if (!values.TryGetValue(field.FieldId, out var value)
                    || (uint)value["offset"] != field.Offset
                    || (uint)value["size"] != field.Size
                    || (byte)value["alignment"] != field.Alignment
                    || (byte)value["encoding"] != (byte)field.Encoding)
                    throw new ArgumentException("Generated layout field packing differs from its canonical materialization.");
            }
        }

        private static void ValidateBindings(JArray declared, IReadOnlyList<GeneratedBindingDescriptor> bindings)
        {
            var values = declared.Cast<JObject>().ToDictionary(value => (string)value["id"], StringComparer.Ordinal);
            if (values.Count != bindings.Count) throw new ArgumentException("Generated binding sets differ.");
            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                if (!values.TryGetValue(binding.BindingId, out var value)
                    || ParseHex((string)value["numericId"]) != binding.NumericBindingId
                    || (uint)value["ordinal"] != binding.Ordinal)
                    throw new ArgumentException("Generated binding identity/ordinal differs from its canonical materialization.");
            }
        }

        private static void ValidateBindingTypeShape(JObject value, string typeId, uint version, RegisteredUnmanagedTypeDescriptor registered)
        {
            var size = (uint)value["size"];
            var alignment = (byte)value["alignment"];
            var encoding = FieldEncoding(value["encoding"]);
            var schemaHash = (string)value["schemaHash"];
            var equality = ParseHex((string)value["equalityContractId"]);
            if (registered.IsValid)
            {
                if (encoding != GeneratedFieldEncoding.Registered
                    || equality != GeneratedNodeMetadata.CanonicalBytesEqualityContractId
                    || !GeneratedTypeRecordHash.IsHash(schemaHash))
                    throw new ArgumentException("Registered binding type metadata is not the accepted codec/equality shape.");
                return;
            }
            if (!GeneratedTypeLayoutRules.TryBuiltIn(typeId, out var expectedSize, out var expectedAlignment, out var expectedEncoding)
                || version != 1 || size != expectedSize || alignment != expectedAlignment || encoding != expectedEncoding
                || !string.IsNullOrEmpty((string)value["schemaId"])
                || schemaHash != GeneratedNodeMetadata.ZeroHash || equality != 0)
                throw new ArgumentException("Built-in binding type metadata differs from the closed generated ABI.");
        }

        private static void RequireDeclaredHash(string declared, CompiledHash actual, string name)
        {
            if (!GeneratedTypeRecordHash.IsHash(declared) || declared != actual.HexadecimalValue)
                throw new ArgumentException("Generated " + name + " differs from the canonical materialization.");
        }

        private static string RegisteredId(string value, string label)
        {
            if (!NodeTypeIdRules.IsValid(value)) throw new ArgumentException("Registered " + label + " IDs must use canonical project-qualified grammar.");
            return value;
        }

        private static NodeManifest ReadManifest(JObject value, IList<GeneratedStorageField> configuration, IList<GeneratedStorageField> memory)
        {
            var parameters = new List<NodeParameterContract>();
            foreach (var property in ((JObject)value["parameters"]).Properties())
            {
                var contract = (JObject)property.Value;
                parameters.Add(new NodeParameterContract(property.Name, ParameterType((string)contract["type"]), (bool)contract["required"]));
            }
            var configFields = configuration.Select(field => new NodeConfigurationField(field.FieldId, field.Offset, field.Size, field.Alignment, field.Encoding == GeneratedFieldEncoding.GeneratedHandle)).ToArray();
            var child = (JObject)value["childPolicy"];
            var statuses = ((JArray)value["possibleStatuses"]).Select(item => Status((string)item)).ToArray();
            var examples = ((JArray)value["examples"]).Cast<JObject>().Select(example => new NodeManifestExample(
                (string)example["title"], example["parameters"].ToString(Formatting.None), (string)example["expectedBehavior"])).ToArray();
            var memoryJson = (JObject)value["memory"]; var configJson = (JObject)value["configuration"];
            return new NodeManifest((string)value["typeId"], (uint)value["version"], (string)value["summary"], (string)value["category"],
                Behavior((string)value["kind"]), (string)value["whenToUse"], (string)value["whenNotToUse"], Domain((string)value["executionDomain"]),
                (bool)value["deterministic"], parameters,
                new NodeChildPolicy((uint)child["minimum"], child["maximum"].Type == JTokenType.Null ? (uint?)null : (uint)child["maximum"], (bool)child["ordered"]),
                Strings((JArray)value["reads"]), Strings((JArray)value["writes"]), Strings((JArray)value["sideEffects"]), statuses,
                new NodeMemoryDescriptor((uint)memoryJson["size"], (byte)memoryJson["alignment"], Lifetime((string)memoryJson["lifetime"])),
                new NodeConfigurationDescriptor((uint)configJson["size"], (byte)configJson["alignment"], configFields),
                Cancellation((string)value["cancellation"]), Cost((string)value["costHint"]), examples,
                value["deprecated"] != null && (bool)value["deprecated"], (string)value["replacementTypeId"]);
        }

        private static string[] Strings(JArray value) => value.Select(item => (string)item).ToArray();
        private static NodeParameterType ParameterType(string value)
        {
            switch (value) { case "boolean": return NodeParameterType.Boolean; case "uint32": return NodeParameterType.UInt32; case "uint64": return NodeParameterType.UInt64; case "string-enum": return NodeParameterType.StringEnum; default: throw Unknown("parameter type", value); }
        }
        private static NodeBehaviorKind Behavior(string value)
        {
            switch (value) { case "condition": return NodeBehaviorKind.Condition; case "action": return NodeBehaviorKind.Action; case "decorator": return NodeBehaviorKind.Decorator; case "composite": return NodeBehaviorKind.Composite; default: throw Unknown("behavior kind", value); }
        }
        private static NodeExecutionDomain Domain(string value)
        {
            switch (value) { case "burst": return NodeExecutionDomain.Burst; case "managed": return NodeExecutionDomain.Managed; case "main-thread": return NodeExecutionDomain.MainThread; default: throw Unknown("execution domain", value); }
        }
        private static NodeMemoryLifetime Lifetime(string value)
        {
            switch (value) { case "activation": return NodeMemoryLifetime.Activation; case "instance": return NodeMemoryLifetime.Instance; default: throw Unknown("memory lifetime", value); }
        }
        private static NodeCancellationMode Cancellation(string value)
        {
            switch (value) { case "not-applicable": return NodeCancellationMode.NotApplicable; case "abort-only": return NodeCancellationMode.AbortOnly; case "command": return NodeCancellationMode.Command; default: throw Unknown("cancellation mode", value); }
        }
        private static NodeCostHint Cost(string value)
        {
            switch (value) { case "trivial": return NodeCostHint.Trivial; case "low": return NodeCostHint.Low; case "medium": return NodeCostHint.Medium; case "high": return NodeCostHint.High; case "variable": return NodeCostHint.Variable; default: throw Unknown("cost hint", value); }
        }
        private static NodeStatus Status(string value)
        {
            switch (value) { case "failure": return NodeStatus.Failure; case "running": return NodeStatus.Running; case "success": return NodeStatus.Success; default: throw Unknown("node status", value); }
        }
        private static GeneratedFieldEncoding FieldEncoding(JToken value) => ClosedByte<GeneratedFieldEncoding>(value, "field encoding");
        private static GeneratedTypeRole TypeRole(JToken value) => ClosedByte<GeneratedTypeRole>(value, "type role");
        private static GeneratedBindingKind BindingKind(JToken value) => ClosedByte<GeneratedBindingKind>(value, "binding kind");
        private static GeneratedPhaseCapability Phase(JToken value)
        {
            var parsed = (GeneratedPhaseCapability)RequireByte(value, "phase capability");
            const GeneratedPhaseCapability all = GeneratedPhaseCapability.Execute | GeneratedPhaseCapability.Cancel | GeneratedPhaseCapability.Completion;
            if ((parsed & ~all) != 0) throw Unknown("phase capability", value.ToString(Formatting.None));
            return parsed;
        }
        private static BlackboardScope BindingScope(JToken value)
        {
            var raw = RequireByte(value, "binding scope");
            if (raw == byte.MaxValue) return (BlackboardScope)byte.MaxValue;
            var parsed = (BlackboardScope)raw;
            if (!Enum.IsDefined(typeof(BlackboardScope), parsed)) throw Unknown("binding scope", raw.ToString(CultureInfo.InvariantCulture));
            return parsed;
        }
        private static T ClosedByte<T>(JToken value, string label) where T : struct
        {
            var parsed = (T)Enum.ToObject(typeof(T), RequireByte(value, label));
            if (!Enum.IsDefined(typeof(T), parsed)) throw Unknown(label, value.ToString(Formatting.None));
            return parsed;
        }
        private static byte RequireByte(JToken value, string label)
        {
            if (value == null || value.Type != JTokenType.Integer) throw Unknown(label, value?.ToString(Formatting.None) ?? "null");
            try { return checked((byte)value.Value<long>()); }
            catch (Exception exception) when (exception is OverflowException || exception is FormatException) { throw Unknown(label, value.ToString(Formatting.None)); }
        }
        private static ArgumentException Unknown(string label, string value) => new ArgumentException("Unknown generated " + label + " '" + value + "'.");
        private static void RequireMembers(JObject value, string label, IReadOnlyList<string> expected)
        {
            var properties = value.Properties().ToArray();
            if (properties.Length != expected.Count) throw new ArgumentException("Generated " + label + " has a noncanonical member set.");
            for (var index = 0; index < expected.Count; index++)
                if (!string.Equals(properties[index].Name, expected[index], StringComparison.Ordinal))
                    throw new ArgumentException("Generated " + label + " has noncanonical member order.");
        }
        private static string Identity(string id, uint version) => id + "\0" + version.ToString(CultureInfo.InvariantCulture);
        private static string EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;
        private static ulong ParseHex(string value) => ulong.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        private static void RequireHash(string content, string expected, string parameterName)
        {
            if (!GeneratedTypeRecordHash.IsHash(expected) || StableHash.Sha256Hex(content) != expected)
                throw new ArgumentException("Generated metadata hash mismatch.", parameterName);
        }
    }
}
