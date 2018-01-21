// Operator overloads not supported in Windows Runtime Components, we use 'double' type instead
#if !WINDOWS_UWP
using System;

namespace UnitsNet
{
    /// <summary>
    ///     Pass it any numeric value (int, double, decimal, float..) and it will be implicitly converted to a <see cref="decimal" />, the quantity value representation used in UnitsNet.
    ///     This is used to avoid an explosion of overloads for methods taking N numeric types for all our 500+ units.
    /// </summary>
    /// <remarks>
    ///     At the time of this writing, this reduces the number of From() overloads to 1/4th:
    ///     From 8 (int, long, double, decimal + each nullable) down to 2 (QuantityValue and QuantityValue?).
    ///     This also adds more numeric types with no extra overhead, such as float, short and byte.
    /// </remarks>
    public struct QuantityValueDecimal
    {
        private readonly decimal _value;

        // Obsolete is used to communicate how they should use this type, instead of making the constructor private and have them figure it out
        [Obsolete("Do not use this constructor. Instead pass any numeric value such as int, long, float, double, decimal, short or byte directly and it will be implicitly casted to decimal.")]
        private QuantityValueDecimal(decimal val)
        {
            _value = val;
        }

        #region To QuantityValue

#pragma warning disable 618
        public static implicit operator QuantityValueDecimal(double val) => new QuantityValueDecimal(Convert.ToDecimal(val));
        public static implicit operator QuantityValueDecimal(float val) => new QuantityValueDecimal(Convert.ToDecimal(val));
        public static implicit operator QuantityValueDecimal(long val) => new QuantityValueDecimal(val);
        public static implicit operator QuantityValueDecimal(decimal val) => new QuantityValueDecimal(val);
        public static implicit operator QuantityValueDecimal(short val) => new QuantityValueDecimal(val);
        public static implicit operator QuantityValueDecimal(byte val) => new QuantityValueDecimal(val);
#pragma warning restore 618

        #endregion

        #region To decimal

        public static explicit operator decimal(QuantityValueDecimal number) => number._value;

        #endregion
    }
}
#endif
