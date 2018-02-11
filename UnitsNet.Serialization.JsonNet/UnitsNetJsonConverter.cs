﻿// Copyright (c) 2013 Andreas Gullberg Larsen (andreas.larsen84@gmail.com).
// https://github.com/angularsen/UnitsNet
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnitsNet.InternalHelpers;

namespace UnitsNet.Serialization.JsonNet
{
    /// <inheritdoc />
    /// <summary>
    ///     A JSON.net <see cref="T:Newtonsoft.Json.JsonConverter" /> for converting to/from JSON and Units.NET
    ///     units like <see cref="T:UnitsNet.Length" /> and <see cref="T:UnitsNet.Mass" />.
    /// </summary>
    /// <remarks>
    ///     Relies on reflection and the type names and namespaces as of 3.x.x of Units.NET.
    ///     Assumptions by reflection code in the converter:
    ///     * Unit classes are of type UnitsNet.Length etc.
    ///     * Unit enums are of type UnitsNet.Units.LengthUnit etc.
    ///     * Unit class has a BaseUnit property returning the base unit, such as LengthUnit.Meter
    /// </remarks>
    public class UnitsNetJsonConverter : JsonConverter
    {
        /// <summary>
        /// Numeric value field of a quantity, typically of type double or decimal.
        /// </summary>
        private const string ValueFieldName = "_value";

        /// <summary>
        ///     Reads the JSON representation of the object.
        /// </summary>
        /// <param name="reader">The <see cref="T:Newtonsoft.Json.JsonReader" /> to read from.</param>
        /// <param name="objectType">Type of the object.</param>
        /// <param name="existingValue">The existing value of object being read.</param>
        /// <param name="serializer">The calling serializer.</param>
        /// <returns>
        ///     The object value.
        /// </returns>
        /// <exception cref="UnitsNetException">Unable to parse value and unit from JSON.</exception>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            if (reader.ValueType != null)
            {
                return reader.Value;
            }
            object obj = TryDeserializeIComparable(reader, serializer);
            // A null System.Nullable value or a comparable type was deserialized so return this
            if (!(obj is ValueUnit vu))
            {
                return obj;
            }

            // "MassUnit.Kilogram" => "MassUnit" and "Kilogram"
            string unitEnumTypeName = vu.Unit.Split('.')[0];
            string unitEnumValue = vu.Unit.Split('.')[1];

            // "MassUnit" => "Mass"
            string quantityTypeName = unitEnumTypeName.Substring(0, unitEnumTypeName.Length - "Unit".Length);

            // "UnitsNet.Units.MassUnit,UnitsNet"
            string unitEnumTypeAssemblyQualifiedName = "UnitsNet.Units." + unitEnumTypeName + ",UnitsNet";

            // "UnitsNet.Mass,UnitsNet"
            string quantityTypeAssemblyQualifiedName = "UnitsNet." + quantityTypeName + ",UnitsNet";

            // -- see http://stackoverflow.com/a/6465096/1256096 for details
            Type unitEnumType = Type.GetType(unitEnumTypeAssemblyQualifiedName);
            if (unitEnumType == null)
            {
                var ex = new UnitsNetException("Unable to find enum type.");
                ex.Data["type"] = unitEnumTypeAssemblyQualifiedName;
                throw ex;
            }

            Type quantityType = Type.GetType(quantityTypeAssemblyQualifiedName);
            if (quantityType == null)
            {
                var ex = new UnitsNetException("Unable to find unit type.");
                ex.Data["type"] = quantityTypeAssemblyQualifiedName;
                throw ex;
            }

            double value = vu.Value;
            object unitValue = Enum.Parse(unitEnumType, unitEnumValue); // Ex: MassUnit.Kilogram

            return CreateQuantity(quantityType, value, unitValue);
        }

        /// <summary>
        /// Creates a quantity (ex: Mass) based on the reflected quantity type, a numeric value and a unit value (ex: MassUnit.Kilogram).
        /// </summary>
        /// <param name="quantityType">Type of quantity, such as <see cref="Mass"/>.</param>
        /// <param name="value">Numeric value.</param>
        /// <param name="unitValue">The unit, such as <see cref="MassUnit.Kilogram"/>.</param>
        /// <returns>The constructed quantity, such as <see cref="Mass"/>.</returns>
        private static object CreateQuantity(Type quantityType, double value, object unitValue)
        {
            // We want the non-nullable return type, example candidates if quantity type is Mass:
            // double Mass.From(double, MassUnit)
            // double? Mass.From(double?, MassUnit)
            MethodInfo notNullableFromMethod = quantityType
                .GetDeclaredMethods()
                .Single(m => m.Name == "From" && Nullable.GetUnderlyingType(m.ReturnType) == null);

            // Either of type QuantityValue or QuantityValueDecimal
            object quantityValue = GetFromMethodValueArgument(notNullableFromMethod, value);

            // Ex: Mass.From(55, MassUnit.Gram)
            // TODO: there is a possible loss of precision if base value requires higher precision than double can represent.
            // Example: Serializing Information.FromExabytes(100) then deserializing to Information 
            // will likely return a very different result. Not sure how we can handle this?
            return notNullableFromMethod.Invoke(null, new[] {quantityValue, unitValue});
        }

        /// <summary>
        /// Returns numeric value wrapped as <see cref="QuantityValue"/> or <see cref="QuantityValueDecimal"/>, depending
        /// on what type the first parameter. Two examples are <see cref="Mass.From(UnitsNet.QuantityValue,UnitsNet.Units.MassUnit)"/> and
        /// <see cref="Information.From(UnitsNet.QuantityValueDecimal,UnitsNet.Units.InformationUnit)"/>.
        /// </summary>
        /// <param name="fromMethod">The reflected From(value, unit) method.</param>
        /// <param name="value">The value to convert to the correct wrapper type.</param>
        /// <returns></returns>
        private static object GetFromMethodValueArgument(MethodInfo fromMethod, double value)
        {
            Type valueParameterType = fromMethod.GetParameters()[0].ParameterType;
            if (valueParameterType == typeof(QuantityValue))
            {
                // Implicit cast: we use this type to avoid explosion of method overloads to handle multiple number types
                return (QuantityValue) value;
            }

            if (valueParameterType == typeof(QuantityValueDecimal))
            {
                return (QuantityValueDecimal) value;
            }

            throw new Exception(
                $"The first parameter of the reflected quantity From() method was expected to be either UnitsNet.QuantityValue or UnitsNet.QuantityValueDecimal, but was instead {valueParameterType}.");
        }

        private static object TryDeserializeIComparable(JsonReader reader, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);
            if (!token.HasValues || token[nameof(ValueUnit.Unit)] == null || token[nameof(ValueUnit.Value)] == null)
            {
                JsonSerializer localSerializer = new JsonSerializer()
                {
                    TypeNameHandling = serializer.TypeNameHandling,
                };
                return token.ToObject<IComparable>(localSerializer);
            }
            else
            {
                return new ValueUnit()
                {
                    Unit = token[nameof(ValueUnit.Unit)].ToString(),
                    Value = token[nameof(ValueUnit.Value)].ToObject<double>()
                };
            }
        }

        /// <summary>
        ///     Writes the JSON representation of the object.
        /// </summary>
        /// <param name="writer">The <see cref="T:Newtonsoft.Json.JsonWriter" /> to write to.</param>
        /// <param name="obj">The value to write.</param>
        /// <param name="serializer">The calling serializer.</param>
        /// <exception cref="UnitsNetException">Can't serialize 'null' value.</exception>
        public override void WriteJson(JsonWriter writer, object obj, JsonSerializer serializer)
        {
            Type quantityType = obj.GetType();

            // ValueUnit should be written as usual (but read in a custom way)
            if(quantityType == typeof(ValueUnit))
            {
                JsonSerializer localSerializer = new JsonSerializer()
                {
                    TypeNameHandling = serializer.TypeNameHandling,
                };
                JToken t = JToken.FromObject(obj, localSerializer);
                
                t.WriteTo(writer);
                return;
            }

            object quantityValue = GetValueOfQuantity(obj, quantityType); // double or decimal value
            string quantityUnitName = GetUnitFullNameOfQuantity(obj, quantityType); // Example: "MassUnit.Kilogram"

            serializer.Serialize(writer, new ValueUnit
            {
                // This might throw OverflowException for very large values?
                // TODO Should we serialize long, decimal and long differently?
                Value = Convert.ToDouble(quantityValue),
                Unit = quantityUnitName
            });
        }

        /// <summary>
        /// Given quantity (ex: <see cref="Mass"/>), returns the full name (ex: "MassUnit.Kilogram") of the constructed unit given by the <see cref="Mass.Unit"/> property.
        /// </summary>
        /// <param name="obj">Quantity, such as <see cref="Mass"/>.</param>
        /// <param name="quantityType">The type of <paramref name="obj"/>, passed in here to reuse a previous lookup.</param>
        /// <returns>"MassUnit.Kilogram" for a mass quantity whose Unit property is MassUnit.Kilogram.</returns>
        private static string GetUnitFullNameOfQuantity(object obj, Type quantityType)
        {
            // Get value of Unit property
            PropertyInfo unitProperty = quantityType.GetPropety("Unit");
            Enum quantityUnit = (Enum) unitProperty.GetValue(obj, null); // MassUnit.Kilogram

            Type unitType = quantityUnit.GetType(); // MassUnit
            return $"{unitType.Name}.{quantityUnit}"; // "MassUnit.Kilogram"
        }

        private static object GetValueOfQuantity(object value, Type quantityType)
        {
            FieldInfo valueField = GetPrivateInstanceField(quantityType, ValueFieldName);

            // Unit base type can be double, long or decimal,
            // so make sure we serialize the real type to avoid
            // loss of precision
            object quantityValue = valueField.GetValue(value);
            return quantityValue;
        }

        private static FieldInfo GetPrivateInstanceField(Type quantityType, string fieldName)
        {
            FieldInfo baseValueField;
            try
            {
                baseValueField = quantityType
#if (NETSTANDARD1_0)
                    .GetTypeInfo()
                    .DeclaredFields
                    .Where(f => !f.IsPublic && !f.IsStatic)
#else
                    .GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
#endif
                    .SingleOrDefault(f => f.Name == fieldName);
            }
            catch (InvalidOperationException)
            {
                var ex = new UnitsNetException($"Expected exactly one private field named [{fieldName}], but found multiple.");
                ex.Data["type"] = quantityType;
                ex.Data["fieldName"] = fieldName;
                throw ex;
            }

            if (baseValueField == null)
            {
                var ex = new UnitsNetException("No private fields found in type.");
                ex.Data["type"] = quantityType;
                ex.Data["fieldName"] = fieldName;
                throw ex;
            }

            return baseValueField;
        }

        /// <summary>
        ///     A structure used to serialize/deserialize Units.NET unit instances.
        /// </summary>
        /// <remarks>
        ///     TODO Units may use decimal, long or double as base value type and might result
        ///     in a loss of precision when serializing/deserializing to decimal.
        ///     Decimal is the highest precision type available in .NET, but has a smaller
        ///     range than double.
        /// </remarks>
        private class ValueUnit
        {
            public string Unit { get; [UsedImplicitly] set; }
            public double Value { get; [UsedImplicitly] set; }
        }

#region Can Convert

        /// <summary>
        ///     Determines whether this instance can convert the specified object type.
        /// </summary>
        /// <param name="objectType">Type of the object.</param>
        /// <returns></returns>
        public override bool CanConvert(Type objectType)
        {
            if (IsNullable(objectType))
            {
                return CanConvertNullable(objectType);
            }

            return objectType.Namespace != null &&
                (objectType.Namespace.Equals(nameof(UnitsNet)) ||
                objectType == typeof(ValueUnit) ||
                // All unit types implement IComparable
                objectType == typeof(IComparable));
        }

        /// <summary>
        ///     Determines whether the specified object type is actually a <see cref="System.Nullable" /> type.
        /// </summary>
        /// <param name="objectType">Type of the object.</param>
        /// <returns><c>true</c> if the object type is nullable; otherwise <c>false</c>.</returns>
        private static bool IsNullable(Type objectType)
        {
            return Nullable.GetUnderlyingType(objectType) != null;
        }

        /// <summary>
        ///     Determines whether this instance can convert the specified nullable object type.
        /// </summary>
        /// <param name="objectType">Type of the object.</param>
        /// <returns><c>true</c> if the object type is a nullable container for a UnitsNet type; otherwise <c>false</c>.</returns>
        protected virtual bool CanConvertNullable(Type objectType)
        {
            // Need to look at the FullName in order to determine if the nullable type contains a UnitsNet type.
            // For example: FullName = 'System.Nullable`1[[UnitsNet.Frequency, UnitsNet, Version=3.19.0.0, Culture=neutral, PublicKeyToken=null]]'
            return objectType.FullName != null && objectType.FullName.Contains(nameof(UnitsNet) + ".");
        }

#endregion
    }
}
