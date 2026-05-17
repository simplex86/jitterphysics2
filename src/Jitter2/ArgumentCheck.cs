/*
 * Jitter2 Physics Library
 * (c) Thorben Linneweber and contributors
 * SPDX-License-Identifier: MIT
 */

using System;
using Jitter2.LinearMath;

namespace Jitter2;

/// <summary>
/// Validates public API inputs that must be checked in all build configurations.
/// </summary>
/// <remarks>
/// Use this helper for construction, setup, and configuration values that would otherwise leave
/// Jitter2 in an invalid persistent state. For Debug-only sanity checks on runtime mutation paths,
/// use <see cref="DebugCheck"/>.
/// </remarks>
internal static class ArgumentCheck
{
    private const Real NonZeroLengthSquared = (Real)1e-12;
    private const Real UnitLengthSquaredTolerance = (Real)1e-3;

    public static void IsFinite(Real value, string paramName)
    {
        if (!IsFiniteCore(value))
        {
            throw new ArgumentException("Value must be finite.", paramName);
        }
    }

    public static void IsFinite(JAngle value, string paramName)
    {
        IsFinite((Real)value, paramName);
    }

    public static void IsNotNaN(Real value, string paramName)
    {
        if (Real.IsNaN(value))
        {
            throw new ArgumentException("Value must not be NaN.", paramName);
        }
    }

    public static void IsNotNaN(JAngle value, string paramName)
    {
        IsNotNaN((Real)value, paramName);
    }

    public static void IsFinite(in JVector value, string paramName)
    {
        if (!IsFiniteCore(value))
        {
            throw new ArgumentException("Vector components must be finite.", paramName);
        }
    }

    public static void IsFinite(in JQuaternion value, string paramName)
    {
        if (!IsFiniteCore(value))
        {
            throw new ArgumentException("Quaternion components must be finite.", paramName);
        }
    }

    public static void IsFinite(in JMatrix value, string paramName)
    {
        if (!IsFiniteCore(value))
        {
            throw new ArgumentException("Matrix components must be finite.", paramName);
        }
    }

    public static void IsNonNegative(Real value, string paramName)
    {
        if (!IsFiniteCore(value) || value < (Real)0.0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be finite and non-negative.");
        }
    }

    public static void IsPositive(Real value, string paramName)
    {
        if (!IsFiniteCore(value) || value <= (Real)0.0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be finite and positive.");
        }
    }

    public static void IsInRange(Real value, Real min, Real max, string paramName)
    {
        if (!IsFiniteCore(value) || value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"Value must be finite and in the range [{min}, {max}].");
        }
    }

    public static void IsNonZero(in JVector value, string paramName)
    {
        if (!IsFiniteCore(value) || value.LengthSquared() <= NonZeroLengthSquared)
        {
            throw new ArgumentException("Vector must be finite and non-zero.", paramName);
        }
    }

    public static void IsUnitVector(in JVector value, string paramName)
    {
        Real lengthSquared = value.LengthSquared();

        if (!IsFiniteCore(value) || MathR.Abs(lengthSquared - (Real)1.0) > UnitLengthSquaredTolerance)
        {
            throw new ArgumentException("Vector must be finite and normalized.", paramName);
        }
    }

    public static void IsUnitQuaternion(in JQuaternion value, string paramName)
    {
        Real lengthSquared = value.LengthSquared();

        if (!IsFiniteCore(value) || MathR.Abs(lengthSquared - (Real)1.0) > UnitLengthSquaredTolerance)
        {
            throw new ArgumentException("Quaternion must be finite and normalized.", paramName);
        }
    }

    private static bool IsFiniteCore(Real value) => Real.IsFinite(value);

    private static bool IsFiniteCore(in JVector value) =>
        IsFiniteCore(value.X) &&
        IsFiniteCore(value.Y) &&
        IsFiniteCore(value.Z);

    private static bool IsFiniteCore(in JQuaternion value) =>
        IsFiniteCore(value.X) &&
        IsFiniteCore(value.Y) &&
        IsFiniteCore(value.Z) &&
        IsFiniteCore(value.W);

    private static bool IsFiniteCore(in JMatrix value) =>
        IsFiniteCore(value.M11) &&
        IsFiniteCore(value.M12) &&
        IsFiniteCore(value.M13) &&
        IsFiniteCore(value.M21) &&
        IsFiniteCore(value.M22) &&
        IsFiniteCore(value.M23) &&
        IsFiniteCore(value.M31) &&
        IsFiniteCore(value.M32) &&
        IsFiniteCore(value.M33);
}
