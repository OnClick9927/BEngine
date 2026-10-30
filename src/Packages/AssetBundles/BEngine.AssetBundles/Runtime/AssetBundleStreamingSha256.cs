using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace BEngine.AssetBundles;

// Per-read browser fallback. Owns only the SHA state and one partial block;
// callers append bounded blocks and validate decoded EOF before finalization.
internal sealed class AssetBundleStreamingSha256 : IDisposable
{
    internal const ulong MaximumDataLength = ulong.MaxValue / 8;
    private readonly uint[] _state = new uint[8];
    private readonly byte[] _tail = new byte[64];
    private ulong _totalBytes;
    private int _tailLength;
    private bool _finalized, _disposed;
    private static ReadOnlySpan<uint> Constants => [
        0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
        0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
        0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
        0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
        0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
        0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
        0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
        0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2];


    internal AssetBundleStreamingSha256() => Reset();

    internal void Append(ReadOnlySpan<byte> data)
    {
        ThrowIfUnavailable();
        if ((ulong)data.Length > MaximumDataLength - _totalBytes)
            throw new ArgumentOutOfRangeException(nameof(data), "SHA-256 input length exceeds its 64-bit bit-length encoding.");
        _totalBytes += (ulong)data.Length;
        if (_tailLength != 0)
        {
            var take = Math.Min(64 - _tailLength, data.Length);
            data[..take].CopyTo(_tail.AsSpan(_tailLength));
            _tailLength += take;
            data = data[take..];
            if (_tailLength != 64) return;
            Compress(_tail, _state);
            _tailLength = 0;
        }
        while (data.Length >= 64)
        {
            Compress(data[..64], _state);
            data = data[64..];
        }
        if (!data.IsEmpty)
        {
            data.CopyTo(_tail);
            _tailLength = data.Length;
        }
    }

    internal void FinalizeHash(Span<byte> destination)
    {
        ThrowIfUnavailable();
        if (destination.Length < 32)
            throw new ArgumentException("The digest requires at least 32 bytes.", nameof(destination));
        _tail[_tailLength] = 0x80;
        _tail.AsSpan(_tailLength + 1).Clear();
        if (_tailLength >= 56)
        {
            Compress(_tail, _state);
            _tail.AsSpan().Clear();
        }
        BinaryPrimitives.WriteUInt64BigEndian(_tail.AsSpan(56), _totalBytes * 8);
        Compress(_tail, _state);
        for (var index=0; index<8; index++)
            BinaryPrimitives.WriteUInt32BigEndian(destination[(index*4)..], _state[index]);
        _finalized = true;
        _tail.AsSpan().Clear();
    }

    internal void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _state[0]=0x6a09e667; _state[1]=0xbb67ae85;
        _state[2]=0x3c6ef372; _state[3]=0xa54ff53a;
        _state[4]=0x510e527f; _state[5]=0x9b05688c;
        _state[6]=0x1f83d9ab; _state[7]=0x5be0cd19;
        _tail.AsSpan().Clear(); _tailLength=0; _totalBytes=0; _finalized=false;
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finalized) throw new InvalidOperationException("The SHA-256 state has already been finalized; reset it before reuse.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed=true; _state.AsSpan().Clear(); _tail.AsSpan().Clear(); _totalBytes=0; _tailLength=0;
    }

    private static void Compress(ReadOnlySpan<byte> block, Span<uint> state)
    {
        Span<uint> schedule = stackalloc uint[64];
        for (var index = 0; index < 16; index++)
            schedule[index] = BinaryPrimitives.ReadUInt32BigEndian(block[(index * 4)..]);
        unchecked
        {
            for (var index = 16; index < 64; index++)
            {
                var x = schedule[index - 15]; var y = schedule[index - 2];
                var s0 = BitOperations.RotateRight(x, 7) ^ BitOperations.RotateRight(x, 18) ^ (x >> 3);
                var s1 = BitOperations.RotateRight(y, 17) ^ BitOperations.RotateRight(y, 19) ^ (y >> 10);
                schedule[index] = schedule[index - 16] + s0 + schedule[index - 7] + s1;
            }
            var a=state[0];var b=state[1];var c=state[2];var d=state[3];
            var e=state[4];var f=state[5];var g=state[6];var h=state[7];
            var constants=Constants;
            for (var index=0;index<64;index++)
            {
                var sum1=BitOperations.RotateRight(e,6)^BitOperations.RotateRight(e,11)^BitOperations.RotateRight(e,25);
                var choose=(e&f)^(~e&g);
                var first=h+sum1+choose+constants[index]+schedule[index];
                var sum0=BitOperations.RotateRight(a,2)^BitOperations.RotateRight(a,13)^BitOperations.RotateRight(a,22);
                var majority=(a&b)^(a&c)^(b&c);
                var second=sum0+majority;
                h=g;g=f;f=e;e=d+first;d=c;c=b;b=a;a=first+second;
            }
            state[0]+=a;state[1]+=b;state[2]+=c;state[3]+=d;
            state[4]+=e;state[5]+=f;state[6]+=g;state[7]+=h;
        }
    }
}
