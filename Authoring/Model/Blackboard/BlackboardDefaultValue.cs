using System;

namespace AIBT.Authoring
{
    public sealed class BlackboardDefaultValue
    {
        private readonly BlackboardValue _runtimeValue;
        private readonly SemanticObject _registeredValue;
        private readonly byte[] _registeredBytes;

        private BlackboardDefaultValue(
            BlackboardValueType valueType,
            BlackboardValue runtimeValue,
            bool isCanonical,
            string enumContract = null,
            string sourceText = null,
            string registeredTypeId = null,
            uint registeredTypeVersion = 0,
            SemanticObject registeredValue = null,
            byte[] registeredBytes = null)
        {
            ValueType = valueType;
            _runtimeValue = runtimeValue;
            IsCanonical = isCanonical;
            EnumContract = enumContract;
            SourceText = sourceText;
            RegisteredTypeId = registeredTypeId;
            RegisteredTypeVersion = registeredTypeVersion;
            _registeredValue = registeredValue;
            _registeredBytes = registeredBytes == null ? null : (byte[])registeredBytes.Clone();
        }

        public BlackboardValueType ValueType { get; }

        public bool IsCanonical { get; }

        public string EnumContract { get; }

        public string SourceText { get; }

        public string RegisteredTypeId { get; }

        public uint RegisteredTypeVersion { get; }

        public bool HasRuntimeValue => _runtimeValue.IsValid;

        public static BlackboardDefaultValue Bool(bool value)
            => Runtime(BlackboardValue.FromBool(value));

        public static BlackboardDefaultValue Int32(int value)
            => Runtime(BlackboardValue.FromInt32(value));

        public static BlackboardDefaultValue Int64(long value)
            => Runtime(BlackboardValue.FromInt64(value));

        public static BlackboardDefaultValue Float32(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return InvalidNumber(BlackboardValueType.Float32);
            }

            return new BlackboardDefaultValue(
                BlackboardValueType.Float32,
                BlackboardValue.FromFloat32(value),
                true);
        }

        public static BlackboardDefaultValue Float64(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return InvalidNumber(BlackboardValueType.Float64);
            }

            return new BlackboardDefaultValue(
                BlackboardValueType.Float64,
                BlackboardValue.FromFloat64(value),
                true);
        }

        public static BlackboardDefaultValue Float2(float x, float y)
            => Vector(BlackboardValueType.Float2, new[] { (double)x, y }, () => BlackboardValue.FromFloat2(new Float2Value(x, y)));

        public static BlackboardDefaultValue Float3(float x, float y, float z)
            => Vector(BlackboardValueType.Float3, new[] { (double)x, y, z }, () => BlackboardValue.FromFloat3(new Float3Value(x, y, z)));

        public static BlackboardDefaultValue Quaternion(float x, float y, float z, float w)
            => Vector(
                BlackboardValueType.Quaternion,
                new[] { (double)x, y, z, w },
                () => BlackboardValue.FromQuaternion(new QuaternionValue(x, y, z, w)));

        public static BlackboardDefaultValue Enum32(string contract, int value)
        {
            if (string.IsNullOrWhiteSpace(contract))
            {
                return new BlackboardDefaultValue(BlackboardValueType.Enum32, default, false, contract);
            }

            return new BlackboardDefaultValue(
                BlackboardValueType.Enum32,
                BlackboardValue.FromEnum32(new Enum32Value(StableHash.Fnv1A64(contract), value)),
                true,
                contract);
        }

        public static BlackboardDefaultValue FixedString32(string value)
            => FixedString(BlackboardValueType.FixedString32, value, BlackboardFixedStringCapacity.FixedString32);

        public static BlackboardDefaultValue FixedString64(string value)
            => FixedString(BlackboardValueType.FixedString64, value, BlackboardFixedStringCapacity.FixedString64);

        public static BlackboardDefaultValue FixedString128(string value)
            => FixedString(BlackboardValueType.FixedString128, value, BlackboardFixedStringCapacity.FixedString128);

        public static BlackboardDefaultValue FixedString512(string value)
            => FixedString(BlackboardValueType.FixedString512, value, BlackboardFixedStringCapacity.FixedString512);

        public static BlackboardDefaultValue AgentId(AIBT.AgentId value)
            => Identity(
                BlackboardValueType.AgentId,
                value.IsValid,
                () => BlackboardValue.FromAgentId(value));

        public static BlackboardDefaultValue EntityId(AIBT.EntityId value)
            => Identity(
                BlackboardValueType.EntityId,
                value.IsValid,
                () => BlackboardValue.FromEntityId(value));

        public static BlackboardDefaultValue OperationId(AIBT.OperationId value)
            => Identity(
                BlackboardValueType.OperationId,
                value.IsValid,
                () => BlackboardValue.FromOperationId(value));

        public static BlackboardDefaultValue AssetId(AIBT.AssetId value)
            => Identity(
                BlackboardValueType.AssetId,
                value.IsValid,
                () => BlackboardValue.FromAssetId(value));

        public static BlackboardDefaultValue RegisteredSource(
            string canonicalTypeId,
            uint version,
            string schemaValueJson)
        {
            return new BlackboardDefaultValue(
                BlackboardValueType.Registered,
                default,
                false,
                sourceText: schemaValueJson,
                registeredTypeId: canonicalTypeId,
                registeredTypeVersion: version);
        }

        internal static BlackboardDefaultValue RegisteredCanonical(
            string canonicalTypeId,
            uint version,
            SemanticObject value,
            byte[] canonicalBytes)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (canonicalBytes == null || canonicalBytes.Length == 0) throw new ArgumentException("Canonical registered bytes are required.", nameof(canonicalBytes));
            return new BlackboardDefaultValue(
                BlackboardValueType.Registered,
                default,
                true,
                registeredTypeId: canonicalTypeId,
                registeredTypeVersion: version,
                registeredValue: value,
                registeredBytes: canonicalBytes);
        }

        public bool TryGetRuntimeValue(out BlackboardValue value)
        {
            value = _runtimeValue;
            return IsCanonical && _runtimeValue.IsValid;
        }

        public bool TryGetRegisteredValue(out SemanticObject value)
        {
            value = _registeredValue;
            return IsCanonical && ValueType == BlackboardValueType.Registered && value != null;
        }

        internal byte[] GetRegisteredBytesCopy()
            => _registeredBytes == null ? null : (byte[])_registeredBytes.Clone();

        private static BlackboardDefaultValue Runtime(BlackboardValue value)
            => new BlackboardDefaultValue(value.Type, value, true);

        private static BlackboardDefaultValue InvalidNumber(BlackboardValueType valueType)
            => new BlackboardDefaultValue(valueType, default, false);

        private static BlackboardDefaultValue Identity(
            BlackboardValueType valueType,
            bool isValid,
            Func<BlackboardValue> create)
        {
            return isValid ? Runtime(create()) : new BlackboardDefaultValue(valueType, default, false);
        }

        private static BlackboardDefaultValue Vector(
            BlackboardValueType valueType,
            double[] components,
            Func<BlackboardValue> create)
        {
            for (var index = 0; index < components.Length; index++)
            {
                if (double.IsNaN(components[index]) || double.IsInfinity(components[index]))
                {
                    return InvalidNumber(valueType);
                }
            }

            return Runtime(create());
        }

        private static BlackboardDefaultValue FixedString(
            BlackboardValueType valueType,
            string value,
            int capacity)
        {
            if (!TryGetUtf8ByteCount(value, out var byteCount) || byteCount > capacity)
            {
                return new BlackboardDefaultValue(valueType, default, false, sourceText: value);
            }

            BlackboardValue runtimeValue;
            switch (valueType)
            {
                case BlackboardValueType.FixedString32:
                    runtimeValue = BlackboardValue.FromString32(value);
                    break;
                case BlackboardValueType.FixedString64:
                    runtimeValue = BlackboardValue.FromString64(value);
                    break;
                case BlackboardValueType.FixedString128:
                    runtimeValue = BlackboardValue.FromString128(value);
                    break;
                case BlackboardValueType.FixedString512:
                    runtimeValue = BlackboardValue.FromString512(value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(valueType));
            }

            return new BlackboardDefaultValue(valueType, runtimeValue, true, sourceText: value);
        }

        private static bool TryGetUtf8ByteCount(string value, out int byteCount)
        {
            byteCount = 0;
            if (value == null)
            {
                return false;
            }

            try
            {
                byteCount = new System.Text.UTF8Encoding(false, true).GetByteCount(value);
                return true;
            }
            catch (System.Text.EncoderFallbackException)
            {
                return false;
            }
        }
    }
}
