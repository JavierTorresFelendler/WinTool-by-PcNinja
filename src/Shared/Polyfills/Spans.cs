// Minimal Span<T> / ReadOnlySpan<T> / BinaryPrimitives for .NET Framework 4.8, so the
// ported USN-journal parsing in IsoFileProof compiles unchanged. Array-backed only;
// supports Length, indexing and C# range slicing (data[8..]), nothing more.
#if NETFRAMEWORK
namespace System
{
    internal readonly struct ReadOnlySpan<T>
    {
        private readonly T[] array;
        private readonly int start;

        public ReadOnlySpan(T[] array, int start, int length)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if ((uint)start > (uint)array.Length || (uint)length > (uint)(array.Length - start)) throw new ArgumentOutOfRangeException(nameof(start));
            this.array = array;
            this.start = start;
            Length = length;
        }

        public int Length { get; }

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Length) throw new IndexOutOfRangeException();
                return array[start + index];
            }
        }

        public ReadOnlySpan<T> Slice(int offset, int length) => new ReadOnlySpan<T>(array, start + offset, length);

        public ReadOnlySpan<T> Slice(int offset) => Slice(offset, Length - offset);

        public static implicit operator ReadOnlySpan<T>(T[] array) => new ReadOnlySpan<T>(array, 0, array.Length);

        public static implicit operator ReadOnlySpan<T>(Span<T> span) => new ReadOnlySpan<T>(span.Array, span.Start, span.Length);
    }

    internal readonly struct Span<T>
    {
        public Span(T[] array, int start, int length)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if ((uint)start > (uint)array.Length || (uint)length > (uint)(array.Length - start)) throw new ArgumentOutOfRangeException(nameof(start));
            Array = array;
            Start = start;
            Length = length;
        }

        internal T[] Array { get; }
        internal int Start { get; }
        public int Length { get; }

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Length) throw new IndexOutOfRangeException();
                return Array[Start + index];
            }
            set
            {
                if ((uint)index >= (uint)Length) throw new IndexOutOfRangeException();
                Array[Start + index] = value;
            }
        }

        public Span<T> Slice(int offset, int length) => new Span<T>(Array, Start + offset, length);

        public static implicit operator Span<T>(T[] array) => new Span<T>(array, 0, array.Length);
    }

    internal static class SpanArrayExtensions
    {
        public static Span<T> AsSpan<T>(this T[] array) => new Span<T>(array, 0, array.Length);
        public static Span<T> AsSpan<T>(this T[] array, int start) => new Span<T>(array, start, array.Length - start);
        public static Span<T> AsSpan<T>(this T[] array, int start, int length) => new Span<T>(array, start, length);
    }
}

namespace System.Buffers.Binary
{
    internal static class BinaryPrimitives
    {
        private static ulong Read(ReadOnlySpan<byte> data, int size)
        {
            if (data.Length < size) throw new ArgumentOutOfRangeException(nameof(data));
            ulong value = 0;
            for (var i = size - 1; i >= 0; i--) value = (value << 8) | data[i];
            return value;
        }

        private static void Write(Span<byte> data, ulong value, int size)
        {
            if (data.Length < size) throw new ArgumentOutOfRangeException(nameof(data));
            for (var i = 0; i < size; i++)
            {
                data[i] = (byte)value;
                value >>= 8;
            }
        }

        public static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> data) => (ushort)Read(data, 2);
        public static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> data) => (uint)Read(data, 4);
        public static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> data) => Read(data, 8);
        public static long ReadInt64LittleEndian(ReadOnlySpan<byte> data) => unchecked((long)Read(data, 8));
        public static int ReadInt32LittleEndian(ReadOnlySpan<byte> data) => unchecked((int)Read(data, 4));

        public static void WriteUInt16LittleEndian(Span<byte> data, ushort value) => Write(data, value, 2);
        public static void WriteUInt32LittleEndian(Span<byte> data, uint value) => Write(data, value, 4);
        public static void WriteUInt64LittleEndian(Span<byte> data, ulong value) => Write(data, value, 8);
        public static void WriteInt64LittleEndian(Span<byte> data, long value) => Write(data, unchecked((ulong)value), 8);
        public static void WriteInt32LittleEndian(Span<byte> data, int value) => Write(data, unchecked((uint)value), 4);
    }
}
#endif
