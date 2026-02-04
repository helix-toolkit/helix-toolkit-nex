/*
The MIT License (MIT)
Copyright (c) 2022 Helix Toolkit contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
 
The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.
 
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

Original code from:
SharpDX project. https://github.com/sharpdx/SharpDX
SlimMath project. http://code.google.com/p/slimmath/

Copyright (c) 2010-2014 SharpDX - Alexandre Mutel
The MIT License (MIT)
Copyright (c) 2007-2011 SlimDX Group
The MIT License (MIT)
*/
namespace HelixToolkit.Nex.Maths
{
    /// <summary>
    /// A half precision (16 bit) floating point value.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Half
    {
        private ushort _value;

        /// <summary>
        /// Number of decimal digits of precision.
        /// </summary>
        public const int PrecisionDigits = 3;

        /// <summary>
        /// Number of bits in the mantissa.
        /// </summary>
        public const int MantissaBits = 11;

        /// <summary>
        /// Maximum decimal exponent.
        /// </summary>
        public const int MaximumDecimalExponent = 4;

        /// <summary>
        /// Maximum binary exponent.
        /// </summary>
        public const int MaximumBinaryExponent = 15;

        /// <summary>
        /// Minimum decimal exponent.
        /// </summary>
        public const int MinimumDecimalExponent = -4;

        /// <summary>
        /// Minimum binary exponent.
        /// </summary>
        public const int MinimumBinaryExponent = -14;

        /// <summary>
        /// Exponent radix.
        /// </summary>
        public const int ExponentRadix = 2;

        /// <summary>
        /// Additional rounding.
        /// </summary>
        public const int AdditionRounding = 1;

        /// <summary>
        /// Smallest such that 1.0 + epsilon != 1.0
        /// </summary>
        public static readonly float Epsilon = 0.0004887581f;

        /// <summary>
        /// Maximum value of the number.
        /// </summary>
        public static readonly float MaxValue = 65504f;

        /// <summary>
        /// Minimum value of the number.
        /// </summary>
        public static readonly float MinValue = 6.103516E-05f;

        /// <summary>
        /// Initializes a new instance of the <see cref = "T:SharpDX.Half" /> structure.
        /// </summary>
        /// <param name = "value">The floating point value that should be stored in 16 bit format.</param>
        public Half(float value)
        {
            this._value = HalfUtils.Pack(value);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref = "T:SharpDX.Half" /> structure.
        /// </summary>
        /// <param name = "rawvalue">The floating point value that should be stored in 16 bit format.</param>
        public Half(ushort rawvalue)
        {
            this._value = rawvalue;
        }

        /// <summary>
        /// Gets or sets the raw 16 bit value used to back this half-float.
        /// </summary>
        public ushort RawValue
        {
            readonly get { return _value; }
            set { this._value = value; }
        }

        /// <summary>
        /// Converts an array of half precision values into full precision values.
        /// </summary>
        /// <param name = "values">The values to be converted.</param>
        /// <returns>An array of converted values.</returns>
        public static float[] ConvertToFloat(Half[] values)
        {
            float[] results = new float[values.Length];
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = HalfUtils.Unpack(values[i].RawValue);
            }

            return results;
        }

        /// <summary>
        /// Converts an array of full precision values into half precision values.
        /// </summary>
        /// <param name = "values">The values to be converted.</param>
        /// <returns>An array of converted values.</returns>
        public static Half[] ConvertToHalf(float[] values)
        {
            Half[] results = new Half[values.Length];
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = new Half(values[i]);
            }

            return results;
        }

        /// <summary>
        /// Performs an explicit conversion from <see cref = "T:System.Single" /> to <see cref = "T:SharpDX.Half" />.
        /// </summary>
        /// <param name = "value">The value to be converted.</param>
        /// <returns>The converted value.</returns>
        public static implicit operator Half(float value)
        {
            return new Half(value);
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref = "T:SharpDX.Half" /> to <see cref = "T:System.Single" />.
        /// </summary>
        /// <param name = "value">The value to be converted.</param>
        /// <returns>The converted value.</returns>
        public static implicit operator float(Half value)
        {
            return HalfUtils.Unpack(value._value);
        }

        /// <summary>
        /// Tests for equality between two objects.
        /// </summary>
        /// <param name = "left">The first value to compare.</param>
        /// <param name = "right">The second value to compare.</param>
        /// <returns>
        /// <c>true</c> if <paramref name = "left" /> has the same value as <paramref name = "right" />; otherwise, <c>false</c>.</returns>
        public static bool operator ==(Half left, Half right)
        {
            return left._value == right._value;
        }

        /// <summary>
        /// Tests for inequality between two objects.
        /// </summary>
        /// <param name = "left">The first value to compare.</param>
        /// <param name = "right">The second value to compare.</param>
        /// <returns>
        /// <c>true</c> if <paramref name = "left" /> has a different value than <paramref name = "right" />; otherwise, <c>false</c>.</returns>
        public static bool operator !=(Half left, Half right)
        {
            return left._value != right._value;
        }

        /// <summary>
        /// Converts the value of the object to its equivalent string representation.
        /// </summary>
        /// <returns>The string representation of the value of this instance.</returns>
        public override readonly string ToString()
        {
            float num = this;
            return num.ToString(CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override readonly int GetHashCode()
        {
            ushort num = _value;
            return (num * 3 / 2) ^ num;
        }

        /// <summary>
        /// Determines whether the specified object instances are considered equal.
        /// </summary>
        /// <param name = "value1" />
        /// <param name = "value2" />
        /// <returns>
        /// <c>true</c> if <paramref name = "value1" /> is the same instance as <paramref name = "value2" /> or
        /// if both are <c>null</c> references or if <c>value1.Equals(value2)</c> returns <c>true</c>; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // MethodImplOptions.AggressiveInlining
        public static bool Equals(ref Half value1, ref Half value2)
        {
            return value1._value == value2._value;
        }

        /// <summary>
        /// Returns a value that indicates whether the current instance is equal to the specified object.
        /// </summary>
        /// <param name = "other">Object to make the comparison with.</param>
        /// <returns>
        /// <c>true</c> if the current instance is equal to the specified object; <c>false</c> otherwise.</returns>
        public readonly bool Equals(Half other)
        {
            return other._value == _value;
        }

        /// <summary>
        /// Returns a value that indicates whether the current instance is equal to a specified object.
        /// </summary>
        /// <param name = "obj">Object to make the comparison with.</param>
        /// <returns>
        /// <c>true</c> if the current instance is equal to the specified object; <c>false</c> otherwise.</returns>
        public override readonly bool Equals(object? obj)
        {
            return obj is Half half && Equals(half);
        }
    }
}
