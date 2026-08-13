using System;
using System.Security.Cryptography;
using System.Text;

namespace AIBT
{
    public static class StableHash
    {
        public const ulong Fnv1A64OffsetBasis = 14695981039346656037;
        public const ulong Fnv1A64Prime = 1099511628211;

        public static ulong Fnv1A64(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return Fnv1A64(Encoding.UTF8.GetBytes(value));
        }

        public static ulong Fnv1A64(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            var hash = Fnv1A64OffsetBasis;
            for (var index = 0; index < bytes.Length; index++)
            {
                hash ^= bytes[index];
                hash = unchecked(hash * Fnv1A64Prime);
            }

            return hash;
        }

        public static string Sha256Hex(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return Sha256Hex(Encoding.UTF8.GetBytes(value));
        }

        public static string Sha256Hex(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            byte[] hash;
            using (var algorithm = SHA256.Create())
            {
                hash = algorithm.ComputeHash(bytes);
            }

            var characters = new char[hash.Length * 2];
            for (var index = 0; index < hash.Length; index++)
            {
                WriteLowercaseHexByte(hash[index], characters, index * 2);
            }

            return new string(characters);
        }

        private static void WriteLowercaseHexByte(byte value, char[] destination, int offset)
        {
            destination[offset] = ToLowercaseHex(value >> 4);
            destination[offset + 1] = ToLowercaseHex(value & 0x0f);
        }

        private static char ToLowercaseHex(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + value - 10);
        }
    }
}
