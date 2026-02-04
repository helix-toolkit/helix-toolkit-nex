namespace HelixToolkit.Nex.Maths
{
    /// <summary>
    /// Represents a bool value with size of 32 bits (4 bytes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct Bool32Bit : IEquatable<Bool32Bit>, IFormattable
    {
        private uint _v;
        public bool V
        {
            readonly get => _v != 0;
            set => _v = value ? 1u : 0;
        }

        public Bool32Bit(bool value)
        {
            _v = value ? 1u : 0;
        }

        public readonly bool Equals(Bool32Bit other)
        {
            return _v == other._v;
        }

        public readonly string ToString(string? format, IFormatProvider? formatProvider)
        {
            return _v.ToString(format, formatProvider);
        }

        public static implicit operator Bool32Bit(bool value)
        {
            return new Bool32Bit(value);
        }

        public static implicit operator bool(Bool32Bit value)
        {
            return value._v != 0;
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is Bool32Bit bit && Equals(bit);
        }

        public static bool operator ==(Bool32Bit left, Bool32Bit right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Bool32Bit left, Bool32Bit right)
        {
            return !(left == right);
        }

        public override readonly int GetHashCode()
        {
            return _v.GetHashCode();
        }
    }
}
