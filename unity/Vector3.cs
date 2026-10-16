// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

// CCSharp --unity-types: bundled UnityEngine types. Same source as Prowl2D's Prowl.Core2D/Unity/Vector3.cs, but with UNITY_NATIVE_OPERATORS defined,
// because CCSharp lowers operators on a struct. Keep the two in step (a change is made in both).

// See the note at the top of Vector2.cs: uncomment to give the C build the operators, once `python3 build.py scan` shows its CCSharp accepts them.
#define UNITY_NATIVE_OPERATORS

using System;

namespace UnityEngine;

/// <summary>
/// UnityEngine.Vector3. The 2D runtime has no depth, so a <see cref="Vector3"/> is what Unity's transform members are typed with (position, scale, euler
/// angles) and z is carried along, not simulated. The differences from Unity are those of <see cref="Vector2"/>: no indexer, and the operators and the
/// conversion to <see cref="Vector2"/> are .NET only until <c>UNITY_NATIVE_OPERATORS</c> is defined.
/// </summary>
public struct Vector3
{
    public float x;
    public float y;
    public float z;

    public Vector3(float px, float py, float pz)
    {
        x = px;
        y = py;
        z = pz;
    }

    /// <summary>As in Unity, z is 0.</summary>
    public Vector3(float px, float py)
    {
        x = px;
        y = py;
        z = 0f;
    }

    public static Vector3 zero { get { return new Vector3(0f, 0f, 0f); } }
    public static Vector3 one { get { return new Vector3(1f, 1f, 1f); } }
    public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
    public static Vector3 down { get { return new Vector3(0f, -1f, 0f); } }
    public static Vector3 left { get { return new Vector3(-1f, 0f, 0f); } }
    public static Vector3 right { get { return new Vector3(1f, 0f, 0f); } }
    public static Vector3 forward { get { return new Vector3(0f, 0f, 1f); } }
    public static Vector3 back { get { return new Vector3(0f, 0f, -1f); } }

    public float sqrMagnitude { get { return x * x + y * y + z * z; } }

    public float magnitude { get { return MathF.Sqrt(x * x + y * y + z * z); } }

    public Vector3 normalized
    {
        get
        {
            float m = MathF.Sqrt(x * x + y * y + z * z);
            if (m > 0.00001f) return new Vector3(x / m, y / m, z / m);
            return new Vector3(0f, 0f, 0f);
        }
    }

    public void Set(float newX, float newY, float newZ)
    {
        x = newX;
        y = newY;
        z = newZ;
    }

    public void Normalize()
    {
        float m = MathF.Sqrt(x * x + y * y + z * z);
        if (m > 0.00001f)
        {
            x = x / m;
            y = y / m;
            z = z / m;
        }
        else
        {
            x = 0f;
            y = 0f;
            z = 0f;
        }
    }

    public void Scale(Vector3 scale)
    {
        x = x * scale.x;
        y = y * scale.y;
        z = z * scale.z;
    }

    public static Vector3 Scale(Vector3 a, Vector3 b)
    {
        return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
    }

    public static float Dot(Vector3 lhs, Vector3 rhs)
    {
        return lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z;
    }

    public static Vector3 Cross(Vector3 lhs, Vector3 rhs)
    {
        return new Vector3(lhs.y * rhs.z - lhs.z * rhs.y, lhs.z * rhs.x - lhs.x * rhs.z, lhs.x * rhs.y - lhs.y * rhs.x);
    }

    public static float Distance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dy = a.y - b.y;
        float dz = a.z - b.z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;
        return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
    }

    public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t)
    {
        return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
    }

    public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)
    {
        float dx = target.x - current.x;
        float dy = target.y - current.y;
        float dz = target.z - current.z;
        float sq = dx * dx + dy * dy + dz * dz;
        if (sq == 0f || (maxDistanceDelta >= 0f && sq <= maxDistanceDelta * maxDistanceDelta)) return target;
        float dist = MathF.Sqrt(sq);
        return new Vector3(current.x + dx / dist * maxDistanceDelta, current.y + dy / dist * maxDistanceDelta, current.z + dz / dist * maxDistanceDelta);
    }

    public static Vector3 Min(Vector3 lhs, Vector3 rhs)
    {
        return new Vector3(MathF.Min(lhs.x, rhs.x), MathF.Min(lhs.y, rhs.y), MathF.Min(lhs.z, rhs.z));
    }

    public static Vector3 Max(Vector3 lhs, Vector3 rhs)
    {
        return new Vector3(MathF.Max(lhs.x, rhs.x), MathF.Max(lhs.y, rhs.y), MathF.Max(lhs.z, rhs.z));
    }

    /// <summary>The unsigned angle between two vectors, in degrees (0..180).</summary>
    public static float Angle(Vector3 a, Vector3 b)
    {
        float denominator = MathF.Sqrt((a.x * a.x + a.y * a.y + a.z * a.z) * (b.x * b.x + b.y * b.y + b.z * b.z));
        if (denominator < 1e-15f) return 0f;
        float d = (a.x * b.x + a.y * b.y + a.z * b.z) / denominator;
        if (d < -1f) d = -1f;
        if (d > 1f) d = 1f;
        return MathF.Acos(d) * 57.29578f;
    }

    public static Vector3 ClampMagnitude(Vector3 vector, float maxLength)
    {
        float sqr = vector.x * vector.x + vector.y * vector.y + vector.z * vector.z;
        if (sqr > maxLength * maxLength)
        {
            float m = MathF.Sqrt(sqr);
            return new Vector3(vector.x / m * maxLength, vector.y / m * maxLength, vector.z / m * maxLength);
        }
        return vector;
    }

    public static Vector3 Reflect(Vector3 inDirection, Vector3 inNormal)
    {
        float factor = -2f * (inNormal.x * inDirection.x + inNormal.y * inDirection.y + inNormal.z * inDirection.z);
        return new Vector3(factor * inNormal.x + inDirection.x, factor * inNormal.y + inDirection.y, factor * inNormal.z + inDirection.z);
    }

    /// <summary>The part of a vector that points along onNormal.</summary>
    public static Vector3 Project(Vector3 vector, Vector3 onNormal)
    {
        float sqrMag = onNormal.x * onNormal.x + onNormal.y * onNormal.y + onNormal.z * onNormal.z;
        if (sqrMag < 1.17549435E-38f) return new Vector3(0f, 0f, 0f);
        float k = (vector.x * onNormal.x + vector.y * onNormal.y + vector.z * onNormal.z) / sqrMag;
        return new Vector3(onNormal.x * k, onNormal.y * k, onNormal.z * k);
    }

    /// <summary>The vector with its part along planeNormal taken out.</summary>
    public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal)
    {
        float sqrMag = planeNormal.x * planeNormal.x + planeNormal.y * planeNormal.y + planeNormal.z * planeNormal.z;
        if (sqrMag < 1.17549435E-38f) return vector;
        float k = (vector.x * planeNormal.x + vector.y * planeNormal.y + vector.z * planeNormal.z) / sqrMag;
        return new Vector3(vector.x - planeNormal.x * k, vector.y - planeNormal.y * k, vector.z - planeNormal.z * k);
    }

    // ---- the operators, by name (for the C build until UNITY_NATIVE_OPERATORS) --------------

    public static Vector3 Add(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
    public static Vector3 Subtract(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
    public static Vector3 Negate(Vector3 a) { return new Vector3(-a.x, -a.y, -a.z); }
    public static Vector3 Multiply(Vector3 a, float d) { return new Vector3(a.x * d, a.y * d, a.z * d); }
    public static Vector3 Divide(Vector3 a, float d) { return new Vector3(a.x / d, a.y / d, a.z / d); }

    /// <summary>Unity's == : true when the two are closer than about 0.00001.</summary>
    public static bool Approximately(Vector3 lhs, Vector3 rhs)
    {
        float dx = lhs.x - rhs.x;
        float dy = lhs.y - rhs.y;
        float dz = lhs.z - rhs.z;
        return dx * dx + dy * dy + dz * dz < 9.99999944E-11f;
    }

#if !CRUST || UNITY_NATIVE_OPERATORS
    public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
    public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
    public static Vector3 operator -(Vector3 a) { return new Vector3(-a.x, -a.y, -a.z); }
    public static Vector3 operator *(Vector3 a, float d) { return new Vector3(a.x * d, a.y * d, a.z * d); }
    public static Vector3 operator *(float d, Vector3 a) { return new Vector3(a.x * d, a.y * d, a.z * d); }
    public static Vector3 operator /(Vector3 a, float d) { return new Vector3(a.x / d, a.y / d, a.z / d); }
    public static bool operator ==(Vector3 lhs, Vector3 rhs)
    {
        float dx = lhs.x - rhs.x;
        float dy = lhs.y - rhs.y;
        float dz = lhs.z - rhs.z;
        return dx * dx + dy * dy + dz * dz < 9.99999944E-11f;
    }
    public static bool operator !=(Vector3 lhs, Vector3 rhs)
    {
        float dx = lhs.x - rhs.x;
        float dy = lhs.y - rhs.y;
        float dz = lhs.z - rhs.z;
        return !(dx * dx + dy * dy + dz * dz < 9.99999944E-11f);
    }
#endif

    // A conversion between the two is an operator on a struct like any other: CCSharp lowers it to a call of the static method (`Vector2::op_Implicit(v)`).
    public static implicit operator Vector2(Vector3 v) { return new Vector2(v.x, v.y); }

#if !CRUST
    public override bool Equals(object other) { return other is Vector3 v && this == v; }
    public override int GetHashCode() { return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2); }
    public override string ToString()
    {
        System.Globalization.CultureInfo c = System.Globalization.CultureInfo.InvariantCulture;
        return "(" + x.ToString("F1", c) + ", " + y.ToString("F1", c) + ", " + z.ToString("F1", c) + ")";
    }
#endif
}
