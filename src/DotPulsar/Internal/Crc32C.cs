/*
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

namespace DotPulsar.Internal
{
    using System;
    using System.Buffers;
#if NETCOREAPP3_1
    using System.Runtime.Intrinsics.X86;

#endif

    public static class Crc32C
    {
        public delegate uint CalculateChecksum(ReadOnlySequence<byte> sequence);

        public static CalculateChecksum Calculate { get; }

        static Crc32C()
        {
#if NETCOREAPP3_1
            if (Crc32CSse.HardwareSupported)
            {
                Calculate = Crc32CSse.Calculate;
                return;
            }
#endif
            Calculate = Crc32Csw.Calculate;
        }
    }

#if NETCOREAPP3_1
    internal static class Crc32CSse
    {
        public static readonly bool HardwareSupported = Sse42.IsSupported;
        private static readonly bool X64Available = Sse42.X64.IsSupported;

        /// <summary>
        /// Computes checksum for span parameter. Stateful, will continue from last hash value
        /// </summary>
        /// <param name="bytes">The input to compute the hash code for.</param>
        public static uint Calculate(ReadOnlySequence<byte> bytes)
        {
            var crc = uint.MaxValue;

            foreach (var readOnlyMemory in bytes)
            {
                var rest = readOnlyMemory.Span;

                if (X64Available)
                {
                    while (rest.Length >= 8)
                    {
                        crc = (uint) Sse42.X64.Crc32(crc, BitConverter.ToUInt64(rest.Slice(0, 8)));
                        rest = rest.Slice(8);
                    }
                }

                while (rest.Length > 0)
                {
                    crc = Sse42.Crc32(crc, rest.Slice(0, 1)[0]);
                    rest = rest.Slice(1);
                }
            }

            return ~crc;
        }
    }
#endif

    internal static class Crc32Csw
    {
        private const uint Generator = 0x82F63B78u;

        private static readonly uint[] Lookup;

        static Crc32Csw()
        {
            Lookup = new uint[16 * 256];

            for (uint i = 0; i < 256; i++)
            {
                var entry = i;

                for (var j = 0; j < 16; j++)
                {
                    for (var k = 0; k < 8; k++)
                        entry = (entry & 1) == 1 ? Generator ^ (entry >> 1) : entry >> 1;

                    Lookup[j * 256 + i] = entry;
                }
            }
        }

        public static uint Calculate(ReadOnlySequence<byte> sequence)
        {
            var block = new uint[16];
            var checksum = uint.MaxValue;
            var remaningBytes = sequence.Length;
            var readingBlock = remaningBytes >= 16;
            var offset = 15;

            foreach (var memory in sequence)
            {
                var span = memory.Span;

                for (var i = 0; i < span.Length; ++i)
                {
                    var currentByte = span[i];

                    if (!readingBlock)
                    {
                        checksum = Lookup[(byte) (checksum ^ currentByte)] ^ (checksum >> 8);
                        continue;
                    }

                    var offSetBase = offset * 256;

                    if (offset > 11)
                        block[offset] = Lookup[offSetBase + ((byte) (checksum >> (8 * (15 - offset))) ^ currentByte)];
                    else
                        block[offset] = Lookup[offSetBase + currentByte];

                    --remaningBytes;

                    if (offset == 0)
                    {
                        offset = 15;
                        readingBlock = remaningBytes >= 16;
                        checksum = 0;

                        for (var j = 0; j < block.Length; ++j)
                            checksum ^= block[j];
                    }
                    else
                    {
                        --offset;
                    }
                }
            }

            return ~checksum;
        }
    }
}
