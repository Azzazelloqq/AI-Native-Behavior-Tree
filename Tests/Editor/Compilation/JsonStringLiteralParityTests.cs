using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Compilation
{
    /// <summary>
    /// P11-016 item 4: the verifier's canonical default text no longer calls Newtonsoft. Its local
    /// escaper (<see cref="JsonStringLiteralV1"/>) must write exactly what <c>JsonConvert.ToString(string)</c>
    /// wrote, as text and as UTF-8 bytes: for every UTF-16 code unit alone and between other text, for null
    /// and empty, for surrogate pairs and lone surrogates, for a seeded random corpus, and for every value
    /// the verifier formats (fixed strings, enum contracts, entity, agent, operation and asset ids).
    /// </summary>
    public sealed class JsonStringLiteralParityTests
    {
        [Test]
        public void EveryCodeUnit_AloneAndBetweenText_MatchesNewtonsoft()
        {
            var mismatches = new List<string>();
            for (var code = 0; code <= 0xFFFF; code++)
            {
                var unit = ((char)code).ToString();
                Compare(unit, mismatches);
                Compare("a" + unit + "b", mismatches);
                Compare(unit + unit, mismatches);
                if (mismatches.Count > 20) break;
            }
            Assert.That(mismatches, Is.Empty, string.Join("\n", mismatches));
        }

        [Test]
        public void NullEmptyAndSurrogates_MatchNewtonsoft()
        {
            Assert.That(JsonStringLiteralV1.Quote(null), Is.EqualTo(JsonConvert.ToString((string)null)));
            Assert.That(JsonStringLiteralV1.Quote(null), Is.EqualTo("\"\""));
            Assert.That(JsonStringLiteralV1.Quote(string.Empty), Is.EqualTo(JsonConvert.ToString(string.Empty)));

            var mismatches = new List<string>();
            // Every high surrogate with a low one, every low surrogate after a high one, and both reversed (two lone units).
            for (var high = 0xD800; high <= 0xDBFF; high++)
            {
                Compare(new string(new[] { (char)high, '\uDC00' }), mismatches);
                Compare(new string(new[] { (char)high, '\uDFFF', '"' }), mismatches);
                Compare(new string(new[] { '\uDC00', (char)high }), mismatches);
            }
            for (var low = 0xDC00; low <= 0xDFFF; low++)
            {
                Compare(new string(new[] { '\uD800', (char)low }), mismatches);
                Compare(new string(new[] { '\n', '\uDBFF', (char)low }), mismatches);
                Compare(new string(new[] { (char)low, '\uD800' }), mismatches);
            }
            Compare("😀 smile \x2028 \u0085 \u007F \u0000", mismatches);
            Assert.That(mismatches, Is.Empty, string.Join("\n", mismatches.Take(20)));
        }

        [Test]
        public void SeededRandomCorpus_MatchesNewtonsoft()
        {
            var random = new Random(0x11016);
            var mismatches = new List<string>();
            var special = new[] { '"', '\\', '\b', '\f', '\n', '\r', '\t', '\u0000', '\u001f', '\u007f', '\u0085', '\x2028', '\x2029', '\uD800', '\uDFFF', '/', '\'' };
            for (var item = 0; item < 20000; item++)
            {
                var length = random.Next(0, 48);
                var units = new char[length];
                for (var index = 0; index < length; index++)
                {
                    switch (random.Next(4))
                    {
                        case 0: units[index] = (char)random.Next(0x20, 0x7F); break;
                        case 1: units[index] = special[random.Next(special.Length)]; break;
                        case 2: units[index] = (char)random.Next(0, 0x10000); break;
                        default: units[index] = (char)random.Next(0, 0x20); break;
                    }
                }
                Compare(new string(units), mismatches);
            }
            Assert.That(mismatches, Is.Empty, string.Join("\n", mismatches.Take(20)));
        }

        [Test]
        public void EveryValueTheVerifierFormats_IsTheSameTextAsWithNewtonsoft()
        {
            foreach (var type in new[] { BlackboardValueType.FixedString32, BlackboardValueType.FixedString64,
                         BlackboardValueType.FixedString128, BlackboardValueType.FixedString512 })
            {
                var size = Size(type);
                foreach (var value in new[] { string.Empty, "plain", "q\"uote\\back/slash", "\u0001\u001f\t\n\r\b\f\u007f",
                             "\u0085\x2028\x2029", "é漢字😀", new string('x', (int)size - 2) })
                {
                    var utf8 = Encoding.UTF8.GetBytes(value);
                    if (utf8.Length > size - 2) continue;
                    var bytes = new byte[size];
                    bytes[0] = (byte)utf8.Length;
                    bytes[1] = (byte)(utf8.Length >> 8);
                    Array.Copy(utf8, 0, bytes, 2, utf8.Length);
                    AssertDefault(type, string.Empty, bytes, JsonConvert.ToString(value));
                }
            }

            foreach (var contract in new[] { "game.mood", "q\"uote\\", "tab\there", "line\x2028sep", "é漢字" })
            {
                var bytes = new byte[Size(BlackboardValueType.Enum32)];
                Write64(bytes, 0, StableHash.Fnv1A64(contract));
                Write32(bytes, 8, unchecked((uint)-7));
                AssertDefault(BlackboardValueType.Enum32, contract, bytes,
                    "{\"contract\":" + JsonConvert.ToString(contract) + ",\"value\":-7}");
            }

            foreach (var id in new[] { 1UL, 42UL, ulong.MaxValue })
            {
                foreach (var type in new[] { BlackboardValueType.EntityId, BlackboardValueType.AgentId })
                {
                    var bytes = new byte[Size(type)];
                    Write64(bytes, 0, id);
                    AssertDefault(type, string.Empty, bytes, JsonConvert.ToString(id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }

                var operation = new byte[Size(BlackboardValueType.OperationId)];
                Write64(operation, 0, id);
                Write32(operation, 8, 3u);
                Write32(operation, 12, uint.MaxValue);
                Write64(operation, 16, ulong.MaxValue - id);
                AssertDefault(BlackboardValueType.OperationId, string.Empty, operation,
                    JsonConvert.ToString(id + ":3:" + uint.MaxValue + ":" + (ulong.MaxValue - id)));
            }

            foreach (var hasLocal in new[] { false, true })
            {
                var bytes = new byte[Size(BlackboardValueType.AssetId)];
                Write64(bytes, 0, 0x0123456789abcdefUL);
                Write64(bytes, 8, 0xfedcba9876543210UL);
                if (hasLocal) Write64(bytes, 16, unchecked((ulong)-12345L));
                bytes[24] = hasLocal ? (byte)1 : (byte)0;
                var asset = new AssetId(0x0123456789abcdefUL, 0xfedcba9876543210UL, hasLocal ? -12345L : 0, hasLocal);
                AssertDefault(BlackboardValueType.AssetId, string.Empty, bytes, hasLocal
                    ? "{\"guid\":" + JsonConvert.ToString(asset.ToGuidString()) + ",\"localFileId\":-12345}"
                    : JsonConvert.ToString(asset.ToGuidString()));
            }
        }

        [Test]
        public void AibtRuntime_DoesNotReferenceNewtonsoft()
        {
            var runtime = typeof(NativeCompiledProgramV2Verifier).Assembly;
            Assert.That(runtime.GetName().Name, Is.EqualTo("AIBT.Runtime"));
            var references = runtime.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
            Assert.That(references.Where(name => name.StartsWith("Newtonsoft", StringComparison.OrdinalIgnoreCase)), Is.Empty,
                "AIBT.Runtime references: " + string.Join(", ", references));

            var root = EditorTestPackagePaths.Resolve("Runtime");
            var users = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => File.ReadLines(path).Any(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                    && (line.Contains("Newtonsoft") || line.Contains("JsonConvert"))))
                .Select(path => path.Substring(root.Length)).ToArray();
            Assert.That(users, Is.Empty, "Runtime sources that use Newtonsoft outside comments.");
        }

        private static void Compare(string value, List<string> mismatches)
        {
            var expected = JsonConvert.ToString(value);
            var actual = JsonStringLiteralV1.Quote(value);
            if (!string.Equals(expected, actual, StringComparison.Ordinal)
                || !Encoding.UTF8.GetBytes(expected).SequenceEqual(Encoding.UTF8.GetBytes(actual)))
                mismatches.Add(string.Join(" ", value.Select(c => ((int)c).ToString("x4"))) + ": expected " + expected + ", actual " + actual);
        }

        private static void AssertDefault(BlackboardValueType type, string contract, byte[] bytes, string expected)
        {
            var actual = NativeCompiledProgramV2Verifier.CanonicalDefaultJson(type, contract, bytes, 0, (uint)bytes.Length);
            Assert.That(actual, Is.EqualTo(Encoding.UTF8.GetBytes(expected)), type + ": " + expected);
        }

        private static uint Size(BlackboardValueType type)
        {
            Assert.That(BuiltInBlackboardTypes.TryGet(type, out var descriptor), Is.True, type.ToString());
            return (uint)descriptor.Size;
        }

        private static void Write32(byte[] bytes, int offset, uint value)
        {
            for (var index = 0; index < 4; index++) bytes[offset + index] = (byte)(value >> (8 * index));
        }

        private static void Write64(byte[] bytes, int offset, ulong value)
        {
            for (var index = 0; index < 8; index++) bytes[offset + index] = (byte)(value >> (8 * index));
        }
    }
}
