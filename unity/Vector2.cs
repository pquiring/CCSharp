// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

// CCSharp --unity-types: bundled UnityEngine types. Same source as Prowl2D's Prowl.Core2D/Unity/Vector2.cs, but with UNITY_NATIVE_OPERATORS defined,
// because CCSharp lowers operators on a struct. Keep the two in step (a change is made in both).

// Operators on a struct (a + b, a * 2f, a == b) are on .NET always. In the C build they need a CCSharp that supports operators on structs (its README says
// current ones do): uncomment the next line, run `python3 build.py scan`, and if it names no refusal in this file the C build has them too.
#define UNITY_NATIVE_OPERATORS

using System;

namespace UnityEngine;

/// <summary>
/// UnityEngine.Vector2: a 2D vector, the same members and meaning as Unity's, in the C# subset that translates to C (see <c>Prowl.Core2D/README.md</c>).
/// <para/>
/// <b>Differences.</b> Two methods of one name must differ in their number of parameters in the subset, and a float cannot be printed there.
/// So there is no indexer; <c>ToString</c>, <c>Equals</c> and the conversions to and from <see cref="Vector3"/> are .NET only; and the operators are
/// .NET only until <c>UNITY_NATIVE_OPERATORS</c> is defined (top of this file). Where the C build has no operator, the method is there under a
/// name: <c>Vector2.Add(a, b)</c> is <c>a + b</c>, and so on.
/// </summary>
public struct Vector2
{
    public float x;
    public float y;

    public Vector2(float px, float py)
    {
        x = px;
        y = py;
    }

    // ---- constants (Unity has them as properties) ------------------------------------------

    public static Vector2 zero { get { return new Vector2(0f, 0f); } }
    public static Vector2 one { get { return new Vector2(1f, 1f); } }
    public static Vector2 up { get { return new Vector2(0f, 1f); } }
    public static Vector2 down { get { return new Vector2(0f, -1f); } }
    public static Vector2 left { get { return new Vector2(-1f, 0f); } }
    public static Vector2 right { get { return new Vector2(1f, 0f); } }

    // ---- the vector's own ------------------------------------------------------------------

    public float sqrMagnitude { get { return x * x + y * y; } }

    public float magnitude { get { return MathF.Sqrt(x * x + y * y); } }

    /// <summary>This vector with a length of 1, or zero if it is too short to have a direction.</summary>
    public Vector2 normalized
    {
        get
        {
            float m = MathF.Sqrt(x * x + y * y);
            if (m > 0.00001f) return new Vector2(x / m, y / m);
            return new Vector2(0f, 0f);
        }
    }

    public void Set(float newX, float newY)
    {
        x = newX;
        y = newY;
    }

    public void Normalize()
    {
        float m = MathF.Sqrt(x * x + y * y);
        if (m > 0.00001f)
        {
            x = x / m;
            y = y / m;
        }
        else
        {
            x = 0f;
            y = 0f;
        }
    }

    /// <summary>Multiplies this vector by another, component by component.</summary>
    public void Scale(Vector2 scale)
    {
        x = x * scale.x;
        y = y * scale.y;
    }

    // ---- between two vectors ---------------------------------------------------------------

    /// <summary>Multiplies two vectors component by component.</summary>
    public static Vector2 Scale(Vector2 a, Vector2 b)
    {
        return new Vector2(a.x * b.x, a.y * b.y);
    }

    public static float Dot(Vector2 lhs, Vector2 rhs)
    {
        return lhs.x * rhs.x + lhs.y * rhs.y;
    }

    public static float Distance(Vector2 a, Vector2 b)
    {
        float dx = a.x - b.x;
        float dy = a.y - b.y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Linear interpolation between two vectors; t is clamped to 0..1.</summary>
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;
        return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
    }

    public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t)
    {
        return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
    }

    public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistanceDelta)
    {
        float dx = target.x - current.x;
        float dy = target.y - current.y;
        float sq = dx * dx + dy * dy;
        if (sq == 0f || (maxDistanceDelta >= 0f && sq <= maxDistanceDelta * maxDistanceDelta)) return target;
        float dist = MathF.Sqrt(sq);
        return new Vector2(current.x + dx / dist * maxDistanceDelta, current.y + dy / dist * maxDistanceDelta);
    }

    public static Vector2 Min(Vector2 lhs, Vector2 rhs)
    {
        return new Vector2(MathF.Min(lhs.x, rhs.x), MathF.Min(lhs.y, rhs.y));
    }

    public static Vector2 Max(Vector2 lhs, Vector2 rhs)
    {
        return new Vector2(MathF.Max(lhs.x, rhs.x), MathF.Max(lhs.y, rhs.y));
    }

    /// <summary>The unsigned angle between two vectors, in degrees (0..180).</summary>
    public static float Angle(Vector2 a, Vector2 b)
    {
        float denominator = MathF.Sqrt((a.x * a.x + a.y * a.y) * (b.x * b.x + b.y * b.y));
        if (denominator < 1e-15f) return 0f;
        float d = (a.x * b.x + a.y * b.y) / denominator;
        if (d < -1f) d = -1f;
        if (d > 1f) d = 1f;
        return MathF.Acos(d) * 57.29578f;
    }

    /// <summary>The angle from a to b in degrees, counter-clockwise positive (-180..180).</summary>
    public static float SignedAngle(Vector2 a, Vector2 b)
    {
        float unsignedAngle = Angle(a, b);
        float crossZ = a.x * b.y - a.y * b.x;
        if (crossZ < 0f) return -unsignedAngle;
        return unsignedAngle;
    }

    /// <summary>The vector, shortened to maxLength if it is longer.</summary>
    public static Vector2 ClampMagnitude(Vector2 vector, float maxLength)
    {
        float sqr = vector.x * vector.x + vector.y * vector.y;
        if (sqr > maxLength * maxLength)
        {
            float m = MathF.Sqrt(sqr);
            return new Vector2(vector.x / m * maxLength, vector.y / m * maxLength);
        }
        return vector;
    }

    /// <summary>The direction a vector goes after bouncing off a surface whose normal is given.</summary>
    public static Vector2 Reflect(Vector2 inDirection, Vector2 inNormal)
    {
        float factor = -2f * (inNormal.x * inDirection.x + inNormal.y * inDirection.y);
        return new Vector2(factor * inNormal.x + inDirection.x, factor * inNormal.y + inDirection.y);
    }

    /// <summary>The vector turned 90 degrees counter-clockwise.</summary>
    public static Vector2 Perpendicular(Vector2 inDirection)
    {
        return new Vector2(-inDirection.y, inDirection.x);
    }

    // ---- the operators, by name (for the C build until UNITY_NATIVE_OPERATORS) --------------

    public static Vector2 Add(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
    public static Vector2 Subtract(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
    public static Vector2 Negate(Vector2 a) { return new Vector2(-a.x, -a.y); }
    public static Vector2 Multiply(Vector2 a, float d) { return new Vector2(a.x * d, a.y * d); }
    public static Vector2 Divide(Vector2 a, float d) { return new Vector2(a.x / d, a.y / d); }

    /// <summary>Unity's == : true when the two are closer than about 0.00001.</summary>
    public static bool Approximately(Vector2 lhs, Vector2 rhs)
    {
        float dx = lhs.x - rhs.x;
        float dy = lhs.y - rhs.y;
        return dx * dx + dy * dy < 9.99999944E-11f;
    }

#if !CRUST || UNITY_NATIVE_OPERATORS
    public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
    public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
    public static Vector2 operator -(Vector2 a) { return new Vector2(-a.x, -a.y); }
    public static Vector2 operator *(Vector2 a, float d) { return new Vector2(a.x * d, a.y * d); }
    public static Vector2 operator *(float d, Vector2 a) { return new Vector2(a.x * d, a.y * d); }
    public static Vector2 operator /(Vector2 a, float d) { return new Vector2(a.x / d, a.y / d); }
    public static bool operator ==(Vector2 lhs, Vector2 rhs)
    {
        float dx = lhs.x - rhs.x;
        float dy = lhs.y - rhs.y;
        return dx * dx + dy * dy < 9.99999944E-11f;
    }
    public static bool operator !=(Vector2 lhs, Vector2 rhs)
    {
        float dx = lhs.x - rhs.x;
        float dy = lhs.y - rhs.y;
        return !(dx * dx + dy * dy < 9.99999944E-11f);
    }
#endif

    // A conversion between the two is an operator on a struct like any other: CCSharp lowers it to a call of the static method (`Vector2::op_Implicit(v)`).
    public static implicit operator Vector3(Vector2 v) { return new Vector3(v.x, v.y, 0f); }

#if !CRUST
    public override bool Equals(object other) { return other is Vector2 v && this == v; }
    public override int GetHashCode() { return x.GetHashCode() ^ (y.GetHashCode() << 2); }
    public override string ToString()
    {
        return "(" + x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ", " + y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ")";
    }
#endif
}
