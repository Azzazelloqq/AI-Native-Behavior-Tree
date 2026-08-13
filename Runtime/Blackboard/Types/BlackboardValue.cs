using System;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace AIBT
{
    public readonly struct BlackboardValue : IEquatable<BlackboardValue>
    {
        private readonly BlackboardValueType _type;
        private readonly BlackboardValueStorage _storage;

        private BlackboardValue(BlackboardValueType type, BlackboardValueStorage storage)
        {
            _type = type;
            _storage = storage;
        }

        public BlackboardValueType Type => _type;

        public bool IsValid
        {
            get
            {
                switch (_type)
                {
                    case BlackboardValueType.Invalid:
                    case BlackboardValueType.Registered:
                        return false;
                    case BlackboardValueType.Float32:
                        return !float.IsNaN(_storage.Float32) && !float.IsInfinity(_storage.Float32);
                    case BlackboardValueType.Float64:
                        return !double.IsNaN(_storage.Float64) && !double.IsInfinity(_storage.Float64);
                    case BlackboardValueType.Enum32:
                        return _storage.Enum32.IsValid;
                    case BlackboardValueType.AgentId:
                        return _storage.AgentId.IsValid;
                    case BlackboardValueType.EntityId:
                        return _storage.EntityId.IsValid;
                    case BlackboardValueType.OperationId:
                        return _storage.OperationId.IsValid;
                    case BlackboardValueType.AssetId:
                        return _storage.AssetId.IsValid;
                    case BlackboardValueType.Bool:
                    case BlackboardValueType.Int32:
                    case BlackboardValueType.Int64:
                    case BlackboardValueType.Float2:
                    case BlackboardValueType.Float3:
                    case BlackboardValueType.Quaternion:
                    case BlackboardValueType.FixedString32:
                    case BlackboardValueType.FixedString64:
                    case BlackboardValueType.FixedString128:
                    case BlackboardValueType.FixedString512:
                        return true;
                    default:
                        return false;
                }
            }
        }

        public static BlackboardValue FromBool(bool value)
        {
            var storage = default(BlackboardValueStorage);
            storage.Bool = value ? (byte)1 : (byte)0;
            return new BlackboardValue(BlackboardValueType.Bool, storage);
        }

        public static BlackboardValue FromInt32(int value)
        {
            var storage = default(BlackboardValueStorage);
            storage.Int32 = value;
            return new BlackboardValue(BlackboardValueType.Int32, storage);
        }

        public static BlackboardValue FromInt64(long value)
        {
            var storage = default(BlackboardValueStorage);
            storage.Int64 = value;
            return new BlackboardValue(BlackboardValueType.Int64, storage);
        }

        public static BlackboardValue FromFloat32(float value)
        {
            var storage = default(BlackboardValueStorage);
            storage.Float32 = BlackboardNumber.Canonicalize(value, nameof(value));
            return new BlackboardValue(BlackboardValueType.Float32, storage);
        }

        public static BlackboardValue FromFloat64(double value)
        {
            var storage = default(BlackboardValueStorage);
            storage.Float64 = BlackboardNumber.Canonicalize(value, nameof(value));
            return new BlackboardValue(BlackboardValueType.Float64, storage);
        }

        public static BlackboardValue FromFloat2(Float2Value value)
        {
            var storage = default(BlackboardValueStorage);
            storage.Float2 = value;
            return new BlackboardValue(BlackboardValueType.Float2, storage);
        }

        public static BlackboardValue FromFloat3(Float3Value value)
        {
            var storage = default(BlackboardValueStorage);
            storage.Float3 = value;
            return new BlackboardValue(BlackboardValueType.Float3, storage);
        }

        public static BlackboardValue FromQuaternion(QuaternionValue value)
        {
            var storage = default(BlackboardValueStorage);
            storage.Quaternion = value;
            return new BlackboardValue(BlackboardValueType.Quaternion, storage);
        }

        public static BlackboardValue FromEnum32(Enum32Value value)
        {
            if (!value.IsValid)
            {
                throw new ArgumentException("A valid enum contract is required.", nameof(value));
            }

            var storage = default(BlackboardValueStorage);
            storage.Enum32 = value;
            return new BlackboardValue(BlackboardValueType.Enum32, storage);
        }

        private static BlackboardValue FromFixedString32(FixedString32Bytes value)
        {
            var storage = default(BlackboardValueStorage);
            storage.FixedString32 = value;
            return new BlackboardValue(BlackboardValueType.FixedString32, storage);
        }

        public static BlackboardValue FromString32(string value)
            => FromFixedString32(new FixedString32Bytes(value));

        private static BlackboardValue FromFixedString64(FixedString64Bytes value)
        {
            var storage = default(BlackboardValueStorage);
            storage.FixedString64 = value;
            return new BlackboardValue(BlackboardValueType.FixedString64, storage);
        }

        public static BlackboardValue FromString64(string value)
            => FromFixedString64(new FixedString64Bytes(value));

        private static BlackboardValue FromFixedString128(FixedString128Bytes value)
        {
            var storage = default(BlackboardValueStorage);
            storage.FixedString128 = value;
            return new BlackboardValue(BlackboardValueType.FixedString128, storage);
        }

        public static BlackboardValue FromString128(string value)
            => FromFixedString128(new FixedString128Bytes(value));

        private static BlackboardValue FromFixedString512(FixedString512Bytes value)
        {
            var storage = default(BlackboardValueStorage);
            storage.FixedString512 = value;
            return new BlackboardValue(BlackboardValueType.FixedString512, storage);
        }

        public static BlackboardValue FromString512(string value)
            => FromFixedString512(new FixedString512Bytes(value));

        public static BlackboardValue FromAgentId(AgentId value)
        {
            RequireValid(value.IsValid, nameof(value), "A valid agent ID is required.");
            var storage = default(BlackboardValueStorage);
            storage.AgentId = value;
            return new BlackboardValue(BlackboardValueType.AgentId, storage);
        }

        public static BlackboardValue FromEntityId(EntityId value)
        {
            RequireValid(value.IsValid, nameof(value), "A valid entity ID is required.");
            var storage = default(BlackboardValueStorage);
            storage.EntityId = value;
            return new BlackboardValue(BlackboardValueType.EntityId, storage);
        }

        public static BlackboardValue FromOperationId(OperationId value)
        {
            RequireValid(value.IsValid, nameof(value), "A valid operation ID is required.");
            var storage = default(BlackboardValueStorage);
            storage.OperationId = value;
            return new BlackboardValue(BlackboardValueType.OperationId, storage);
        }

        public static BlackboardValue FromAssetId(AssetId value)
        {
            RequireValid(value.IsValid, nameof(value), "A valid asset ID is required.");
            var storage = default(BlackboardValueStorage);
            storage.AssetId = value;
            return new BlackboardValue(BlackboardValueType.AssetId, storage);
        }

        public bool TryGetBool(out bool value)
        {
            if (_type == BlackboardValueType.Bool)
            {
                value = _storage.Bool != 0;
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGetInt32(out int value) => TryGet(BlackboardValueType.Int32, _storage.Int32, out value);

        public bool TryGetInt64(out long value) => TryGet(BlackboardValueType.Int64, _storage.Int64, out value);

        public bool TryGetFloat32(out float value) => TryGet(BlackboardValueType.Float32, _storage.Float32, out value);

        public bool TryGetFloat64(out double value) => TryGet(BlackboardValueType.Float64, _storage.Float64, out value);

        public bool TryGetFloat2(out Float2Value value) => TryGet(BlackboardValueType.Float2, _storage.Float2, out value);

        public bool TryGetFloat3(out Float3Value value) => TryGet(BlackboardValueType.Float3, _storage.Float3, out value);

        public bool TryGetQuaternion(out QuaternionValue value)
            => TryGet(BlackboardValueType.Quaternion, _storage.Quaternion, out value);

        public bool TryGetEnum32(out Enum32Value value) => TryGet(BlackboardValueType.Enum32, _storage.Enum32, out value);

        private bool TryGetFixedString32(out FixedString32Bytes value)
            => TryGet(BlackboardValueType.FixedString32, _storage.FixedString32, out value);

        private bool TryGetFixedString64(out FixedString64Bytes value)
            => TryGet(BlackboardValueType.FixedString64, _storage.FixedString64, out value);

        private bool TryGetFixedString128(out FixedString128Bytes value)
            => TryGet(BlackboardValueType.FixedString128, _storage.FixedString128, out value);

        private bool TryGetFixedString512(out FixedString512Bytes value)
            => TryGet(BlackboardValueType.FixedString512, _storage.FixedString512, out value);

        public bool TryGetFixedString(out string value)
        {
            switch (_type)
            {
                case BlackboardValueType.FixedString32:
                    value = _storage.FixedString32.ToString();
                    return true;
                case BlackboardValueType.FixedString64:
                    value = _storage.FixedString64.ToString();
                    return true;
                case BlackboardValueType.FixedString128:
                    value = _storage.FixedString128.ToString();
                    return true;
                case BlackboardValueType.FixedString512:
                    value = _storage.FixedString512.ToString();
                    return true;
                default:
                    value = null;
                    return false;
            }
        }

        public bool TryGetAgentId(out AgentId value) => TryGet(BlackboardValueType.AgentId, _storage.AgentId, out value);

        public bool TryGetEntityId(out EntityId value) => TryGet(BlackboardValueType.EntityId, _storage.EntityId, out value);

        public bool TryGetOperationId(out OperationId value)
            => TryGet(BlackboardValueType.OperationId, _storage.OperationId, out value);

        public bool TryGetAssetId(out AssetId value) => TryGet(BlackboardValueType.AssetId, _storage.AssetId, out value);

        public bool Equals(BlackboardValue other)
        {
            if (_type != other._type)
            {
                return false;
            }

            switch (_type)
            {
                case BlackboardValueType.Invalid: return true;
                case BlackboardValueType.Bool: return _storage.Bool == other._storage.Bool;
                case BlackboardValueType.Int32: return _storage.Int32 == other._storage.Int32;
                case BlackboardValueType.Int64: return _storage.Int64 == other._storage.Int64;
                case BlackboardValueType.Float32: return _storage.Float32 == other._storage.Float32;
                case BlackboardValueType.Float64: return _storage.Float64 == other._storage.Float64;
                case BlackboardValueType.Float2: return _storage.Float2 == other._storage.Float2;
                case BlackboardValueType.Float3: return _storage.Float3 == other._storage.Float3;
                case BlackboardValueType.Quaternion: return _storage.Quaternion == other._storage.Quaternion;
                case BlackboardValueType.Enum32: return _storage.Enum32 == other._storage.Enum32;
                case BlackboardValueType.FixedString32: return _storage.FixedString32.Equals(other._storage.FixedString32);
                case BlackboardValueType.FixedString64: return _storage.FixedString64.Equals(other._storage.FixedString64);
                case BlackboardValueType.FixedString128: return _storage.FixedString128.Equals(other._storage.FixedString128);
                case BlackboardValueType.FixedString512: return _storage.FixedString512.Equals(other._storage.FixedString512);
                case BlackboardValueType.AgentId: return _storage.AgentId == other._storage.AgentId;
                case BlackboardValueType.EntityId: return _storage.EntityId == other._storage.EntityId;
                case BlackboardValueType.OperationId: return _storage.OperationId == other._storage.OperationId;
                case BlackboardValueType.AssetId: return _storage.AssetId == other._storage.AssetId;
                default: return false;
            }
        }

        public override bool Equals(object obj) => obj is BlackboardValue other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var valueHash = GetValueHashCode();
                return ((int)_type * 397) ^ valueHash;
            }
        }

        public static bool operator ==(BlackboardValue left, BlackboardValue right) => left.Equals(right);

        public static bool operator !=(BlackboardValue left, BlackboardValue right) => !left.Equals(right);

        private bool TryGet<T>(BlackboardValueType expectedType, T storedValue, out T value)
            where T : unmanaged
        {
            if (_type == expectedType)
            {
                value = storedValue;
                return true;
            }

            value = default;
            return false;
        }

        private static void RequireValid(bool isValid, string parameterName, string message)
        {
            if (!isValid)
            {
                throw new ArgumentException(message, parameterName);
            }
        }

        private int GetValueHashCode()
        {
            switch (_type)
            {
                case BlackboardValueType.Invalid: return 0;
                case BlackboardValueType.Bool: return _storage.Bool;
                case BlackboardValueType.Int32: return _storage.Int32;
                case BlackboardValueType.Int64: return _storage.Int64.GetHashCode();
                case BlackboardValueType.Float32: return _storage.Float32.GetHashCode();
                case BlackboardValueType.Float64: return _storage.Float64.GetHashCode();
                case BlackboardValueType.Float2: return _storage.Float2.GetHashCode();
                case BlackboardValueType.Float3: return _storage.Float3.GetHashCode();
                case BlackboardValueType.Quaternion: return _storage.Quaternion.GetHashCode();
                case BlackboardValueType.Enum32: return _storage.Enum32.GetHashCode();
                case BlackboardValueType.FixedString32: return _storage.FixedString32.GetHashCode();
                case BlackboardValueType.FixedString64: return _storage.FixedString64.GetHashCode();
                case BlackboardValueType.FixedString128: return _storage.FixedString128.GetHashCode();
                case BlackboardValueType.FixedString512: return _storage.FixedString512.GetHashCode();
                case BlackboardValueType.AgentId: return _storage.AgentId.GetHashCode();
                case BlackboardValueType.EntityId: return _storage.EntityId.GetHashCode();
                case BlackboardValueType.OperationId: return _storage.OperationId.GetHashCode();
                case BlackboardValueType.AssetId: return _storage.AssetId.GetHashCode();
                default: return 0;
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 512)]
        private struct BlackboardValueStorage
        {
            [FieldOffset(0)] public byte Bool;
            [FieldOffset(0)] public int Int32;
            [FieldOffset(0)] public long Int64;
            [FieldOffset(0)] public float Float32;
            [FieldOffset(0)] public double Float64;
            [FieldOffset(0)] public Float2Value Float2;
            [FieldOffset(0)] public Float3Value Float3;
            [FieldOffset(0)] public QuaternionValue Quaternion;
            [FieldOffset(0)] public Enum32Value Enum32;
            [FieldOffset(0)] public FixedString32Bytes FixedString32;
            [FieldOffset(0)] public FixedString64Bytes FixedString64;
            [FieldOffset(0)] public FixedString128Bytes FixedString128;
            [FieldOffset(0)] public FixedString512Bytes FixedString512;
            [FieldOffset(0)] public AgentId AgentId;
            [FieldOffset(0)] public EntityId EntityId;
            [FieldOffset(0)] public OperationId OperationId;
            [FieldOffset(0)] public AssetId AssetId;
        }
    }

    public static class BlackboardFixedStringCapacity
    {
        public const int FixedString32 = 29;
        public const int FixedString64 = 61;
        public const int FixedString128 = 125;
        public const int FixedString512 = 509;
    }
}
