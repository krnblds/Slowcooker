using System.Security.Cryptography;

namespace Slowcooker.Internal;

/// <summary>
/// Time-ordered ids: 48 bits of Unix milliseconds, a 12-bit per-millisecond sequence, 4 version bits and 64 random bits,
/// written as 32 lowercase hex characters. Sorting ids as strings sorts jobs by creation order, even within one
/// millisecond, which keeps FIFO cheap in every storage (it is just an ORDER BY id / lexicographic ZSET tie-break).
/// </summary>
internal static class JobIds
{
    private static readonly Lock Gate = new();
    private static long _lastMs;
    private static int _sequence;

    public static string New()
    {
        long ms;
        int sequence;
        lock (Gate)
        {
            ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (ms <= _lastMs)
            {
                ms = _lastMs;          // clock went backwards or same millisecond: keep monotonic
                if (++_sequence > 0xFFF) { ms = ++_lastMs; _sequence = 0; }
            }
            else
            {
                _lastMs = ms;
                _sequence = 0;
            }
            sequence = _sequence;
        }

        Span<byte> bytes = stackalloc byte[16];
        bytes[0] = (byte)(ms >> 40);
        bytes[1] = (byte)(ms >> 32);
        bytes[2] = (byte)(ms >> 24);
        bytes[3] = (byte)(ms >> 16);
        bytes[4] = (byte)(ms >> 8);
        bytes[5] = (byte)ms;
        bytes[6] = (byte)(0x70 | (sequence >> 8));
        bytes[7] = (byte)sequence;
        RandomNumberGenerator.Fill(bytes[8..]);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return Convert.ToHexStringLower(bytes);
    }
}
