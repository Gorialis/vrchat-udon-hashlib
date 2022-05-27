// -*- coding: utf-8 -*-
/*
MIT License

Copyright (c) 2021-present Devon (Gorialis) R

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

using System;
using System.Linq;
using UnityEngine;
using UdonSharp;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
using UdonSharpEditor;
#endif


public class UdonHashLib : UdonSharpBehaviour
{
    // Udon does not support UTF-8 or expose System.Text.Encoding so we must implement this ourselves
    private byte[] ToUTF8(char[] characters)
    {
        byte[] buffer = new byte[characters.Length * 4];

        int writeIndex = 0;
        for (int i = 0; i < characters.Length; i++)
        {
            uint character = characters[i];

            if (character < 0x80)
            {
                buffer[writeIndex++] = (byte)character;
            } else if (character < 0x800)
            {
                buffer[writeIndex++] = (byte)(0b11000000 | ((character >> 6) & 0b11111));
                buffer[writeIndex++] = (byte)(0b10000000 | (character & 0b111111));
            } else if (character < 0x10000)
            {
                buffer[writeIndex++] = (byte)(0b11100000 | ((character >> 12) & 0b1111));
                buffer[writeIndex++] = (byte)(0b10000000 | ((character >> 6) & 0b111111));
                buffer[writeIndex++] = (byte)(0b10000000 | (character & 0b111111));
            } else
            {
                buffer[writeIndex++] = (byte)(0b11110000 | ((character >> 18) & 0b111));
                buffer[writeIndex++] = (byte)(0b10000000 | ((character >> 12) & 0b111111));
                buffer[writeIndex++] = (byte)(0b10000000 | ((character >> 6) & 0b111111));
                buffer[writeIndex++] = (byte)(0b10000000 | (character & 0b111111));
            }
        }

        // We do this to truncate off the end of the array
        // This would be a lot easier with Array.Resize, but Udon once again does not allow access to it.
        byte[] output = new byte[writeIndex];

        for (int i = 0; i < writeIndex; i++)
            output[i] = buffer[i];

        return output;
    }

    /*  MD5  */

    private readonly ulong[] md5_init =
    {
        0x67452301, 0xefcdab89, 0x98badcfe, 0x10325476,
    };

    private readonly ulong[] md5_constants = {
        0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
        0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
        0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
        0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
        0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
        0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
        0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
        0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
    };

    private readonly int[] md5_shifts =
    {
        7, 12, 17, 22,  7, 12, 17, 22,  7, 12, 17, 22,  7, 12, 17, 22,
        5,  9, 14, 20,  5,  9, 14, 20,  5,  9, 14, 20,  5,  9, 14, 20,
        4, 11, 16, 23,  4, 11, 16, 23,  4, 11, 16, 23,  4, 11, 16, 23,
        6, 10, 15, 21,  6, 10, 15, 21,  6, 10, 15, 21,  6, 10, 15, 21,
    };

    private string MD5_Core(byte[] payload_bytes, ulong[] init, ulong[] constants, int[] shifts, ulong size_mask, int word_size, int chunk_modulo, int appended_length, int round_count, string output_format, int output_segments)
    {
        int word_bytes = word_size / 8;

        // Working variables a0->d0
        ulong[] working_variables = new ulong[4];
        init.CopyTo(working_variables, 0);

        byte[] input = new byte[chunk_modulo];
        ulong[] message_schedule = new ulong[16];

        // Each 64-byte/512-bit chunk
        // 64 bits/8 bytes are required at the end for the bit size
        for (int chunk_index = 0; chunk_index < payload_bytes.Length + appended_length + 1; chunk_index += chunk_modulo)
        {
            int chunk_size = Mathf.Min(chunk_modulo, payload_bytes.Length - chunk_index);
            int schedule_index = 0;

            // Buffer message
            for (; schedule_index < chunk_size; ++schedule_index)
                input[schedule_index] = payload_bytes[chunk_index + schedule_index];
            // Append a 1-bit if not an even chunk
            if (schedule_index < chunk_modulo && chunk_size >= 0)
                input[schedule_index++] = 0b10000000;
            // Pad with zeros until the end
            for (; schedule_index < chunk_modulo; ++schedule_index)
                input[schedule_index] = 0x00;
            // If the chunk is less than 56 bytes, this will be the final chunk containing the data size in bits
            if (chunk_size < chunk_modulo - appended_length)
            {
                ulong bit_size = (ulong)payload_bytes.Length * 8ul;
                input[chunk_modulo - 8] = Convert.ToByte((bit_size >> 0x00) & 0xFFul);
                input[chunk_modulo - 7] = Convert.ToByte((bit_size >> 0x08) & 0xFFul);
                input[chunk_modulo - 6] = Convert.ToByte((bit_size >> 0x10) & 0xFFul);
                input[chunk_modulo - 5] = Convert.ToByte((bit_size >> 0x18) & 0xFFul);
                input[chunk_modulo - 4] = Convert.ToByte((bit_size >> 0x20) & 0xFFul);
                input[chunk_modulo - 3] = Convert.ToByte((bit_size >> 0x28) & 0xFFul);
                input[chunk_modulo - 2] = Convert.ToByte((bit_size >> 0x30) & 0xFFul);
                input[chunk_modulo - 1] = Convert.ToByte((bit_size >> 0x38) & 0xFFul);
            }

            // Copy into w[0..15]
            int copy_index = 0;
            for (; copy_index < 16; copy_index++)
            {
                message_schedule[copy_index] = 0ul;
                for (int i = 0; i < word_bytes; i++)
                {
                    message_schedule[copy_index] = message_schedule[copy_index] | ((ulong)input[(copy_index * word_bytes) + i] << (i * 8));
                }

                message_schedule[copy_index] = message_schedule[copy_index] & size_mask;
            }

            // temp vars
            ulong f, g;
            // work is equivalent to A, B, C, D
            // This copies work from a0, b0, c0, d0
            ulong[] work = new ulong[4];
            working_variables.CopyTo(work, 0);

            // Compression function main loop
            for (copy_index = 0; copy_index < round_count; copy_index++)
            {
                if (copy_index < 16)
                {
                    f = ((work[1] & work[2]) | ((size_mask ^ work[1]) & work[3])) & size_mask;
                    g = (ulong)copy_index;
                } else if (copy_index < 32)
                {
                    f = ((work[3] & work[1]) | ((size_mask ^ work[3]) & work[2])) & size_mask;
                    g = (ulong)(((5 * copy_index) + 1) % 16);
                } else if (copy_index < 48)
                {
                    f = work[1] ^ work[2] ^ work[3];
                    g = (ulong)(((3 * copy_index) + 5) % 16);
                } else
                {
                    f = (work[2] ^ (work[1] | (size_mask ^ work[3]))) & size_mask;
                    g = (ulong)(7 * copy_index % 16);
                }

                f = (f + work[0] + constants[copy_index] + message_schedule[g]) & size_mask;
                work[0] = work[3];
                work[3] = work[2];
                work[2] = work[1];
                work[1] = (work[1] + ((f << shifts[copy_index]) | (f >> word_size - shifts[copy_index]))) & size_mask;
            }

            for (copy_index = 0; copy_index < 4; copy_index++)
                working_variables[copy_index] = (working_variables[copy_index] + work[copy_index]) & size_mask;
        }

        // Finalization
        string output = "";

        for (int character_index = 0; character_index < output_segments; character_index++)
        {
            ulong value = working_variables[character_index];
            output += string.Format(output_format,
                ((value & 0x000000FFul) << 0x18) |
                ((value & 0x0000FF00ul) << 0x08) |
                ((value & 0x00FF0000ul) >> 0x08) |
                ((value & 0xFF000000ul) >> 0x18)
            );
        }

        return output;
    }

    public string MD5_Bytes(byte[] data)
    {
        return MD5_Core(data, md5_init, md5_constants, md5_shifts, 0xFFFFFFFFul, 32, 64, 8, 64, "{0:x8}", 4);
    }

    public string MD5_UTF8(string text)
    {
        return MD5_Core(ToUTF8(text.ToCharArray()), md5_init, md5_constants, md5_shifts, 0xFFFFFFFFul, 32, 64, 8, 64, "{0:x8}", 4);
    }

    /*  SHA1  */

    private readonly ulong[] sha1_init = {
        0x67452301, 0xefcdab89, 0x98badcfe, 0x10325476, 0xc3d2e1f0,
    };

    private string SHA1_Core(byte[] payload_bytes, ulong[] init, ulong size_mask, int word_size, int chunk_modulo, int appended_length, int left_rotations, int round_count, string output_format, int output_segments)
    {
        int word_bytes = word_size / 8;

        // Working variables h0->h4
        ulong[] working_variables = new ulong[5];
        init.CopyTo(working_variables, 0);

        byte[] input = new byte[chunk_modulo];
        ulong[] message_schedule = new ulong[round_count];

        // Each 64-byte/512-bit chunk
        // 64 bits/8 bytes are required at the end for the bit size
        for (int chunk_index = 0; chunk_index < payload_bytes.Length + appended_length + 1; chunk_index += chunk_modulo)
        {
            int chunk_size = Mathf.Min(chunk_modulo, payload_bytes.Length - chunk_index);
            int schedule_index = 0;

            // Buffer message
            for (; schedule_index < chunk_size; ++schedule_index)
                input[schedule_index] = payload_bytes[chunk_index + schedule_index];
            // Append a 1-bit if not an even chunk
            if (schedule_index < chunk_modulo && chunk_size >= 0)
                input[schedule_index++] = 0b10000000;
            // Pad with zeros until the end
            for (; schedule_index < chunk_modulo; ++schedule_index)
                input[schedule_index] = 0x00;
            // If the chunk is less than 56 bytes, this will be the final chunk containing the data size in bits
            if (chunk_size < chunk_modulo - appended_length)
            {
                ulong bit_size = (ulong)payload_bytes.Length * 8ul;
                input[chunk_modulo - 1] = Convert.ToByte((bit_size >> 0x00) & 0xFFul);
                input[chunk_modulo - 2] = Convert.ToByte((bit_size >> 0x08) & 0xFFul);
                input[chunk_modulo - 3] = Convert.ToByte((bit_size >> 0x10) & 0xFFul);
                input[chunk_modulo - 4] = Convert.ToByte((bit_size >> 0x18) & 0xFFul);
                input[chunk_modulo - 5] = Convert.ToByte((bit_size >> 0x20) & 0xFFul);
                input[chunk_modulo - 6] = Convert.ToByte((bit_size >> 0x28) & 0xFFul);
                input[chunk_modulo - 7] = Convert.ToByte((bit_size >> 0x30) & 0xFFul);
                input[chunk_modulo - 8] = Convert.ToByte((bit_size >> 0x38) & 0xFFul);
            }

            // Copy into w[0..15]
            int copy_index = 0;
            for (; copy_index < 16; copy_index++)
            {
                message_schedule[copy_index] = 0ul;
                for (int i = 0; i < word_bytes; i++)
                {
                    message_schedule[copy_index] = (message_schedule[copy_index] << 8) | input[(copy_index * word_bytes) + i];
                }

                message_schedule[copy_index] = message_schedule[copy_index] & size_mask;
            }
            // Extend
            for (; copy_index < round_count; copy_index++)
            {
                ulong w = message_schedule[copy_index - 3] ^ message_schedule[copy_index - 8] ^ message_schedule[copy_index - 14] ^ message_schedule[copy_index - 16];
                message_schedule[copy_index] = (
                    (w << left_rotations) | (w >> word_size - left_rotations)
                ) & size_mask;
            }

            // temp vars
            ulong temp, k, f;
            // work is equivalent to a, b, c, d, e
            // This copies work from h0, h1, h2, h3, h4
            ulong[] work = new ulong[5];
            working_variables.CopyTo(work, 0);

            // Compression function main loop
            for (copy_index = 0; copy_index < round_count; copy_index++)
            {
                if (copy_index < 20)
                {
                    f = ((work[1] & work[2]) | ((size_mask ^ work[1]) & work[3])) & size_mask;
                    k = 0x5A827999;
                } else if (copy_index < 40)
                {
                    f = work[1] ^ work[2] ^ work[3];
                    k = 0x6ED9EBA1;
                } else if (copy_index < 60)
                {
                    f = (work[1] & work[2]) ^ (work[1] & work[3]) ^ (work[2] & work[3]);
                    k = 0x8F1BBCDC;
                } else
                {
                    f = work[1] ^ work[2] ^ work[3];
                    k = 0xCA62C1D6;
                }

                temp = (((work[0] << 5) | (work[0] >> word_size - 5)) + f + work[4] + k + message_schedule[copy_index]) & size_mask;
                work[4] = work[3];
                work[3] = work[2];
                work[2] = ((work[1] << 30) | (work[1] >> word_size - 30)) & size_mask;
                work[1] = work[0];
                work[0] = temp;
            }

            for (copy_index = 0; copy_index < 5; copy_index++)
                working_variables[copy_index] = (working_variables[copy_index] + work[copy_index]) & size_mask;
        }

        // Finalization
        string output = "";

        for (int character_index = 0; character_index < output_segments; character_index++)
        {
            output += string.Format(output_format, working_variables[character_index]);
        }

        return output;
    }

    /* SHA0 */
    /* This algorithm has had documented vulnerabilites since before its formal release. */
    /* Near to nothing uses it, and it should not be trusted for real applications under any circumstances */
    /* It is only here for completeness, and there may be bugs with it due to the difficulty of finding RFC-complying implementations to test against. */
    /*
    public string SHA0_Bytes(byte[] data)
    {
        return SHA1_Core(data, sha1_init, 0xFFFFFFFFul, 32, 64, 8, 0, 80, "{0:x8}", 5);
    }

    public string SHA0_UTF8(string text)
    {
        return SHA1_Core(ToUTF8(text.ToCharArray()), sha1_init, 0xFFFFFFFFul, 32, 64, 8, 0, 80, "{0:x8}", 5);
    }
    */

    /* SHA1 */
    public string SHA1_Bytes(byte[] data)
    {
        return SHA1_Core(data, sha1_init, 0xFFFFFFFFul, 32, 64, 8, 1, 80, "{0:x8}", 5);
    }

    public string SHA1_UTF8(string text)
    {
        return SHA1_Core(ToUTF8(text.ToCharArray()), sha1_init, 0xFFFFFFFFul, 32, 64, 8, 1, 80, "{0:x8}", 5);
    }

    /*  SHA2  */

    private readonly ulong[] sha224_init = {
        0xc1059ed8, 0x367cd507, 0x3070dd17, 0xf70e5939, 0xffc00b31, 0x68581511, 0x64f98fa7, 0xbefa4fa4,
    };

    private readonly ulong[] sha256_init = {
        0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19,
    };

    private readonly ulong[] sha256_constants = {
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
    };

    private readonly int[] sha256_sums =
    {
        7, 18, 3,  // s0
        17, 19, 10,  // s1
    };

    private readonly int[] sha256_sigmas =
    {
        2, 13, 22,  // S0
        6, 11, 25,  // S1
    };

    private readonly ulong[] sha384_init = {
        0xcbbb9d5dc1059ed8, 0x629a292a367cd507, 0x9159015a3070dd17, 0x152fecd8f70e5939, 0x67332667ffc00b31,
        0x8eb44a8768581511, 0xdb0c2e0d64f98fa7, 0x47b5481dbefa4fa4,
    };

    private readonly ulong[] sha512_init = {
        0x6a09e667f3bcc908, 0xbb67ae8584caa73b, 0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1, 0x510e527fade682d1,
        0x9b05688c2b3e6c1f, 0x1f83d9abfb41bd6b, 0x5be0cd19137e2179,
    };

    private readonly ulong[] sha512_constants = {
        0x428a2f98d728ae22, 0x7137449123ef65cd, 0xb5c0fbcfec4d3b2f, 0xe9b5dba58189dbbc, 0x3956c25bf348b538,
        0x59f111f1b605d019, 0x923f82a4af194f9b, 0xab1c5ed5da6d8118, 0xd807aa98a3030242, 0x12835b0145706fbe,
        0x243185be4ee4b28c, 0x550c7dc3d5ffb4e2, 0x72be5d74f27b896f, 0x80deb1fe3b1696b1, 0x9bdc06a725c71235,
        0xc19bf174cf692694, 0xe49b69c19ef14ad2, 0xefbe4786384f25e3, 0x0fc19dc68b8cd5b5, 0x240ca1cc77ac9c65,
        0x2de92c6f592b0275, 0x4a7484aa6ea6e483, 0x5cb0a9dcbd41fbd4, 0x76f988da831153b5, 0x983e5152ee66dfab,
        0xa831c66d2db43210, 0xb00327c898fb213f, 0xbf597fc7beef0ee4, 0xc6e00bf33da88fc2, 0xd5a79147930aa725,
        0x06ca6351e003826f, 0x142929670a0e6e70, 0x27b70a8546d22ffc, 0x2e1b21385c26c926, 0x4d2c6dfc5ac42aed,
        0x53380d139d95b3df, 0x650a73548baf63de, 0x766a0abb3c77b2a8, 0x81c2c92e47edaee6, 0x92722c851482353b,
        0xa2bfe8a14cf10364, 0xa81a664bbc423001, 0xc24b8b70d0f89791, 0xc76c51a30654be30, 0xd192e819d6ef5218,
        0xd69906245565a910, 0xf40e35855771202a, 0x106aa07032bbd1b8, 0x19a4c116b8d2d0c8, 0x1e376c085141ab53,
        0x2748774cdf8eeb99, 0x34b0bcb5e19b48a8, 0x391c0cb3c5c95a63, 0x4ed8aa4ae3418acb, 0x5b9cca4f7763e373,
        0x682e6ff3d6b2b8a3, 0x748f82ee5defb2fc, 0x78a5636f43172f60, 0x84c87814a1f0ab72, 0x8cc702081a6439ec,
        0x90befffa23631e28, 0xa4506cebde82bde9, 0xbef9a3f7b2c67915, 0xc67178f2e372532b, 0xca273eceea26619c,
        0xd186b8c721c0c207, 0xeada7dd6cde0eb1e, 0xf57d4f7fee6ed178, 0x06f067aa72176fba, 0x0a637dc5a2c898a6,
        0x113f9804bef90dae, 0x1b710b35131c471b, 0x28db77f523047d84, 0x32caab7b40c72493, 0x3c9ebe0a15c9bebc,
        0x431d67c49c100d4c, 0x4cc5d4becb3e42b6, 0x597f299cfc657e2a, 0x5fcb6fab3ad6faec, 0x6c44198c4a475817,
    };

    private readonly int[] sha512_sums =
    {
        1, 8, 7,  // s0
        19, 61, 6,  // s1
    };

    private readonly int[] sha512_sigmas =
    {
        28, 34, 39,  // S0
        14, 18, 41,  // S1
    };

    private string SHA2_Core(byte[] payload_bytes, ulong[] init, ulong[] constants, int[] sums, int[] sigmas, ulong size_mask, int word_size, int chunk_modulo, int appended_length, int round_count, string output_format, int output_segments)
    {
        int word_bytes = word_size / 8;

        // Working variables h0->h7
        ulong[] working_variables = new ulong[8];
        init.CopyTo(working_variables, 0);

        byte[] input = new byte[chunk_modulo];
        ulong[] message_schedule = new ulong[round_count];

        // Each 64-byte/512-bit chunk
        // 64 bits/8 bytes are required at the end for the bit size
        for (int chunk_index = 0; chunk_index < payload_bytes.Length + appended_length + 1; chunk_index += chunk_modulo) {
            int chunk_size = Mathf.Min(chunk_modulo, payload_bytes.Length - chunk_index);
            int schedule_index = 0;

            // Buffer message
            for (; schedule_index < chunk_size; ++schedule_index)
                input[schedule_index] = payload_bytes[chunk_index + schedule_index];
            // Append a 1-bit if not an even chunk
            if (schedule_index < chunk_modulo && chunk_size >= 0)
                input[schedule_index++] = 0b10000000;
            // Pad with zeros until the end
            for (; schedule_index < chunk_modulo; ++schedule_index)
                input[schedule_index] = 0x00;
            // If the chunk is less than 56 bytes, this will be the final chunk containing the data size in bits
            if (chunk_size < chunk_modulo - appended_length) {
                ulong bit_size = (ulong)payload_bytes.Length * 8ul;
                input[chunk_modulo - 1] = Convert.ToByte((bit_size >> 0x00) & 0xFFul);
                input[chunk_modulo - 2] = Convert.ToByte((bit_size >> 0x08) & 0xFFul);
                input[chunk_modulo - 3] = Convert.ToByte((bit_size >> 0x10) & 0xFFul);
                input[chunk_modulo - 4] = Convert.ToByte((bit_size >> 0x18) & 0xFFul);
                input[chunk_modulo - 5] = Convert.ToByte((bit_size >> 0x20) & 0xFFul);
                input[chunk_modulo - 6] = Convert.ToByte((bit_size >> 0x28) & 0xFFul);
                input[chunk_modulo - 7] = Convert.ToByte((bit_size >> 0x30) & 0xFFul);
                input[chunk_modulo - 8] = Convert.ToByte((bit_size >> 0x38) & 0xFFul);
            }

            // Copy into w[0..15]
            int copy_index = 0;
            for (; copy_index < 16; copy_index++)
            {
                message_schedule[copy_index] = 0ul;
                for (int i = 0; i < word_bytes; i++)
                {
                    message_schedule[copy_index] = (message_schedule[copy_index] << 8) | input[(copy_index * word_bytes) + i];
                }

                message_schedule[copy_index] = message_schedule[copy_index] & size_mask;
            }
            // Extend
            for(; copy_index < round_count; copy_index++) {
                ulong s0_read = message_schedule[copy_index - 15];
                ulong s1_read = message_schedule[copy_index - 2];

                message_schedule[copy_index] = (
                    message_schedule[copy_index - 16] +
                    (((s0_read >> sums[0]) | (s0_read << word_size - sums[0])) ^ ((s0_read >> sums[1]) | (s0_read << word_size - sums[1])) ^ (s0_read >> sums[2])) + // s0
                    message_schedule[copy_index - 7] +
                    (((s1_read >> sums[3]) | (s1_read << word_size - sums[3])) ^ ((s1_read >> sums[4]) | (s1_read << word_size - sums[4])) ^ (s1_read >> sums[5])) // s1
                ) & size_mask;
            }

            // temp vars
            ulong temp1, temp2;
            // work is equivalent to a, b, c, d, e, f, g, h
            // This copies work from h0, h1, h2, h3, h4, h5, h6, h7
            ulong[] work = new ulong[8];
            working_variables.CopyTo(work, 0);

            // Compression function main loop
            for (copy_index = 0; copy_index < round_count; copy_index++)
            {
                ulong ep1 = ((work[4] >> sigmas[3]) | (work[4] << word_size - sigmas[3])) ^ ((work[4] >> sigmas[4]) | (work[4] << word_size - sigmas[4])) ^ ((work[4] >> sigmas[5]) | (work[4] << word_size - sigmas[5]));
                ulong ch = (work[4] & work[5]) ^ ((size_mask ^ work[4]) & work[6]);
                ulong ep0 = ((work[0] >> sigmas[0]) | (work[0] << word_size - sigmas[0])) ^ ((work[0] >> sigmas[1]) | (work[0] << word_size - sigmas[1])) ^ ((work[0] >> sigmas[2]) | (work[0] << word_size - sigmas[2]));
                ulong maj = (work[0] & work[1]) ^ (work[0] & work[2]) ^ (work[1] & work[2]);
                temp1 = work[7] + ep1 + ch + constants[copy_index] + message_schedule[copy_index];
                temp2 = ep0 + maj;
                work[7] = work[6];
                work[6] = work[5];
                work[5] = work[4];
                work[4] = (work[3] + temp1) & size_mask;
                work[3] = work[2];
                work[2] = work[1];
                work[1] = work[0];
                work[0] = (temp1 + temp2) & size_mask;
            }

            for (copy_index = 0; copy_index < 8; copy_index++)
                working_variables[copy_index] = (working_variables[copy_index] + work[copy_index]) & size_mask;
        }

        // Finalization
        string output = "";

        for (int character_index = 0; character_index < output_segments; character_index++) {
            output += string.Format(output_format, working_variables[character_index]);
        }

        return output;
    }

    /* SHA224 */
    public string SHA224_Bytes(byte[] data)
    {
        return SHA2_Core(data, sha224_init, sha256_constants, sha256_sums, sha256_sigmas, 0xFFFFFFFFul, 32, 64, 8, 64, "{0:x8}", 7);
    }

    public string SHA224_UTF8(string text)
    {
        return SHA2_Core(ToUTF8(text.ToCharArray()), sha224_init, sha256_constants, sha256_sums, sha256_sigmas, 0xFFFFFFFFul, 32, 64, 8, 64, "{0:x8}", 7);
    }

    /* SHA256 */
    public string SHA256_Bytes(byte[] data)
    {
        return SHA2_Core(data, sha256_init, sha256_constants, sha256_sums, sha256_sigmas, 0xFFFFFFFFul, 32, 64, 8, 64, "{0:x8}", 8);
    }

    public string SHA256_UTF8(string text)
    {
        return SHA2_Core(ToUTF8(text.ToCharArray()), sha256_init, sha256_constants, sha256_sums, sha256_sigmas, 0xFFFFFFFFul, 32, 64, 8, 64, "{0:x8}", 8);
    }

    /* SHA384 */
    public string SHA384_Bytes(byte[] data)
    {
        return SHA2_Core(data, sha384_init, sha512_constants, sha512_sums, sha512_sigmas, 0xFFFFFFFFFFFFFFFFul, 64, 128, 16, 80, "{0:x16}", 6);
    }

    public string SHA384_UTF8(string text)
    {
        return SHA2_Core(ToUTF8(text.ToCharArray()), sha384_init, sha512_constants, sha512_sums, sha512_sigmas, 0xFFFFFFFFFFFFFFFFul, 64, 128, 16, 80, "{0:x16}", 6);
    }

    /* SHA512 */
    public string SHA512_Bytes(byte[] data)
    {
        return SHA2_Core(data, sha512_init, sha512_constants, sha512_sums, sha512_sigmas, 0xFFFFFFFFFFFFFFFFul, 64, 128, 16, 80, "{0:x16}", 8);
    }

    public string SHA512_UTF8(string text)
    {
        return SHA2_Core(ToUTF8(text.ToCharArray()), sha512_init, sha512_constants, sha512_sums, sha512_sigmas, 0xFFFFFFFFFFFFFFFFul, 64, 128, 16, 80, "{0:x16}", 8);
    }
}


#if !COMPILER_UDONSHARP && UNITY_EDITOR

[CustomEditor(typeof(UdonHashLib))]
public class UdonHashLibEditor : Editor
{
    private string inputString = "";

    public override void OnInspectorGUI()
    {
        // Draws the default convert to UdonBehaviour button, program asset field, sync settings, etc.
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

        UdonHashLib inspectorBehaviour = (UdonHashLib)target;

        if (!EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("Enter play mode to run tests", MessageType.Info);

        EditorGUI.BeginDisabledGroup(!EditorApplication.isPlaying);

        // A simple string field modification with Undo handling
        EditorGUILayout.LabelField("Input for hash");
        inputString = EditorGUILayout.TextArea(inputString);

        if (GUILayout.Button("Calculate hashes"))
        {

            CheckHash(inputString, true);
        }

        if (GUILayout.Button("Run test suite"))
        {
            int testBatch = 4096;
            int testCount = 0, succeeded = 0;

            System.Random random = new System.Random();
            string chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!\"#$%&\'()*+,-./:;<=>?@[\\]^_`{|}~\n";
            string buffer = new string(Enumerable.Repeat(chars, testBatch).Select(s => s[random.Next(s.Length)]).ToArray());

            Debug.Log($"Buffer generated for ASCII testing: {buffer}");

            for (int i = 0; i < testBatch; i++)
            {
                string test = buffer.Substring(random.Next(buffer.Length - i), i);

                testCount++;
                if (CheckHash(test))
                    succeeded++;
            }

            // Japanese character (UTF-8) test
            chars = "\u3042\u3044\u3046\u3048\u304a\u304b\u304d\u304f\u3051\u3053\u3055\u3057\u3059\u305b\u305d\u305f\u3061\u3064\u3066\u3068\u306a\u306b\u306c\u306d\u306e\u306f\u3072\u3075\u3078\u307b\u307e\u307f\u3080\u3081\u3082\u3084\u3086\u3088\u3089\u308a\u308b\u308c\u308d\u308f\u3090\u3091\u3092\u3093\u30a2\u30a4\u30a6\u30a8\u30aa\u30ab\u30ad\u30af\u30b1\u30b3\u30b5\u30b7\u30b9\u30bb\u30bd\u30bf\u30c1\u30c4\u30c6\u30c8\u30ca\u30cb\u30cc\u30cd\u30ce\u30cf\u30d2\u30d5\u30d8\u30db\u30de\u30df\u30e0\u30e1\u30e2\u30e4\u30e6\u30e8\u30e9\u30ea\u30eb\u30ec\u30ed\u30ef\u30f0\u30f1\u30f2\u30f3";
            buffer = new string(Enumerable.Repeat(chars, testBatch).Select(s => s[random.Next(s.Length)]).ToArray());

            Debug.Log($"Buffer generated for JPN/UTF-8 testing: {buffer}");

            for (int i = 0; i < testBatch; i++)
            {
                string test = buffer.Substring(random.Next(buffer.Length - i), i);

                testCount++;
                if (CheckHash(test))
                    succeeded++;
            }

            EditorUtility.DisplayDialog("Results", $"{testCount} tests ran, {succeeded} succeeded in all hashes", "OK");
        }

        EditorGUI.EndDisabledGroup();
    }

    private bool CheckHash(string input, bool showDialog = false, bool showDialogWhenFailed = true)
    {
        UdonHashLib inspectorBehaviour = (UdonHashLib)target;

        string md5_udon = inspectorBehaviour.MD5_UTF8(input);
        string sha1_udon = inspectorBehaviour.SHA1_UTF8(input);
        string sha224_udon = inspectorBehaviour.SHA224_UTF8(input);
        string sha256_udon = inspectorBehaviour.SHA256_UTF8(input);
        string sha384_udon = inspectorBehaviour.SHA384_UTF8(input);
        string sha512_udon = inspectorBehaviour.SHA512_UTF8(input);

        string md5_csharp = GenerateNonUdon(input, System.Security.Cryptography.MD5.Create());
        string sha1_csharp = GenerateNonUdon(input, System.Security.Cryptography.SHA1.Create());
        string sha256_csharp = GenerateNonUdon(input, System.Security.Cryptography.SHA256.Create());
        string sha384_csharp = GenerateNonUdon(input, System.Security.Cryptography.SHA384.Create());
        string sha512_csharp = GenerateNonUdon(input, System.Security.Cryptography.SHA512.Create());

        bool allPass = (
            md5_udon == md5_csharp &&
            sha1_udon == sha1_csharp &&
            sha256_udon == sha256_csharp &&
            sha384_udon == sha384_csharp &&
            sha512_udon == sha512_csharp
        );

        if (showDialog || (!allPass && showDialogWhenFailed))
            EditorUtility.DisplayDialog("Hashes", string.Join("\n", new string[] {
                input,
                "",
                $"md5 [{(md5_udon == md5_csharp ? "PASS" : "FAIL")}]: {md5_udon}",
                $"sha1 [{(sha1_udon == sha1_csharp ? "PASS" : "FAIL")}]: {sha1_udon}",
                $"sha224: {sha224_udon}", // No system impl for this
                $"sha256 [{(sha256_udon == sha256_csharp ? "PASS" : "FAIL")}]: {sha256_udon}",
                $"sha384 [{(sha384_udon == sha384_csharp ? "PASS" : "FAIL")}]: {sha384_udon}",
                $"sha512 [{(sha512_udon == sha512_csharp ? "PASS" : "FAIL")}]: {sha512_udon}",
            }), "OK");

        return allPass;
    }

    private string GenerateNonUdon(string input, System.Security.Cryptography.HashAlgorithm algorithm)
    {
        using (var hash = algorithm)
        {
            var bytes = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));

            string output = "";

            foreach (byte x in bytes)
            {
                output += string.Format("{0:x2}", x);
            }

            return output;
        }
    }
}
#endif
