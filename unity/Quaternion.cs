// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

// CCSharp --unity-types: bundled UnityEngine types. Same source as Prowl2D's Prowl.Core2D/Unity/Quaternion.cs, but with UNITY_NATIVE_OPERATORS defined,
// because CCSharp lowers operators on a struct. Keep the two in step (a change is made in both).

// See the note at the top of Vector2.cs: uncomment to give the C build the operators, once `python3 build.py scan` shows its CCSharp accepts them.
#define UNITY_NATIVE_OPERATORS

using System;

namespace UnityEngine;

/// <summary>
/// UnityEngine.Quaternion. A 2D node can only turn about the z axis, so what a node stores is that angle; a quaternion is how Unity's
/// <c>transform.rotation</c> says it, and <c>Quaternion.Euler(0, 0, a)</c> is the usual way to build one. The maths here is the general one (Unity's
/// z, x, y order for euler angles), so a rotation about x or y can be made and composed, but a node given one keeps only its z part.
/// <para/>
/// The operators (<c>a * b</c> composes, <c>q * v</c> turns a vector) are .NET only until <c>UNITY_NATIVE_OPERATORS</c> is defined; by name they are
/// <see cref="Multiply"/> and <see cref="RotateVector"/>.
/// </summary>
public struct Quaternion
{
    public float x;
    public float y;
    public float z;
    public float w;

    public Quaternion(float px, float py, float pz, float pw)
    {
        x = px;
        y = py;
        z = pz;
        w = pw;
    }

    public static Quaternion identity { get { return new Quaternion(0f, 0f, 0f, 1f); } }

    public void Set(float newX, float newY, float newZ, float newW)
    {
        x = newX;
        y = newY;
        z = newZ;
        w = newW;
    }

    /// <summary>The rotation that turns z degrees about z, then x degrees about x, then y degrees about y (Unity's order).</summary>
    public static Quaternion Euler(float xDegrees, float yDegrees, float zDegrees)
    {
        float hx = xDegrees * 0.008726646f;
        float hy = yDegrees * 0.008726646f;
        float hz = zDegrees * 0.008726646f;
        float cx = MathF.Cos(hx);
        float sx = MathF.Sin(hx);
        float cy = MathF.Cos(hy);
        float sy = MathF.Sin(hy);
        float cz = MathF.Cos(hz);
        float sz = MathF.Sin(hz);
        return new Quaternion(
            cy * sx * cz + sy * cx * sz,
            sy * cx * cz - cy * sx * sz,
            cy * cx * sz - sy * sx * cz,
            cy * cx * cz + sy * sx * sz);
    }

    public static Quaternion Euler(Vector3 euler)
    {
        return Euler(euler.x, euler.y, euler.z);
    }

    /// <summary>A rotation of angle degrees about an axis.</summary>
    public static Quaternion AngleAxis(float angle, Vector3 axis)
    {
        float len = MathF.Sqrt(axis.x * axis.x + axis.y * axis.y + axis.z * axis.z);
        if (len < 1e-6f) return new Quaternion(0f, 0f, 0f, 1f);
        float half = angle * 0.008726646f;
        float s = MathF.Sin(half) / len;
        return new Quaternion(axis.x * s, axis.y * s, axis.z * s, MathF.Cos(half));
    }

    /// <summary>This rotation as euler angles in degrees, each 0..360 (Unity's order: z, then x, then y).</summary>
    public Vector3 eulerAngles
    {
        get
        {
            float m11 = 1f - 2f * (y * y + z * z);
            float m21 = 2f * (x * y + z * w);
            float m22 = 1f - 2f * (x * x + z * z);
            float m23 = 2f * (y * z - x * w);
            float m31 = 2f * (x * z - y * w);
            float m13 = 2f * (x * z + y * w);
            float m33 = 1f - 2f * (x * x + y * y);
            float sinX = -m23;
            if (sinX > 1f) sinX = 1f;
            if (sinX < -1f) sinX = -1f;
            float ex = MathF.Asin(sinX);
            float ey;
            float ez;
            if (sinX > 0.999999f || sinX < -0.999999f)
            {
                ez = 0f;
                ey = MathF.Atan2(-m31, m11);
            }
            else
            {
                ez = MathF.Atan2(m21, m22);
                ey = MathF.Atan2(m13, m33);
            }
            return new Vector3(Wrap360(ex * 57.29578f), Wrap360(ey * 57.29578f), Wrap360(ez * 57.29578f));
        }
    }

    private static float Wrap360(float d)
    {
        d = d % 360f;
        if (d < 0f) d = d + 360f;
        if (d >= 360f) d = d - 360f;
        return d;
    }

    public static float Dot(Quaternion a, Quaternion b)
    {
        return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
    }

    /// <summary>The angle in degrees between two rotations.</summary>
    public static float Angle(Quaternion a, Quaternion b)
    {
        float d = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        if (d < 0f) d = -d;
        if (d > 1f) d = 1f;
        return MathF.Acos(d) * 2f * 57.29578f;
    }

    /// <summary>The rotation that undoes this one (of a unit quaternion, its conjugate).</summary>
    public static Quaternion Inverse(Quaternion rotation)
    {
        float n = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
        if (n < 1e-30f) return new Quaternion(0f, 0f, 0f, 1f);
        return new Quaternion(-rotation.x / n, -rotation.y / n, -rotation.z / n, rotation.w / n);
    }

    public static Quaternion Normalize(Quaternion q)
    {
        float m = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (m < 1e-15f) return new Quaternion(0f, 0f, 0f, 1f);
        return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
    }

    public void Normalize()
    {
        float m = MathF.Sqrt(x * x + y * y + z * z + w * w);
        if (m < 1e-15f)
        {
            x = 0f;
            y = 0f;
            z = 0f;
            w = 1f;
            return;
        }
        x = x / m;
        y = y / m;
        z = z / m;
        w = w / m;
    }

    /// <summary>Normalised linear interpolation along the shorter way round; t is clamped to 0..1.</summary>
    public static Quaternion Lerp(Quaternion a, Quaternion b, float t)
    {
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;
        return LerpUnclamped(a, b, t);
    }

    public static Quaternion LerpUnclamped(Quaternion a, Quaternion b, float t)
    {
        float s = 1f - t;
        float bx = b.x;
        float by = b.y;
        float bz = b.z;
        float bw = b.w;
        if (a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w < 0f)
        {
            bx = -bx;
            by = -by;
            bz = -bz;
            bw = -bw;
        }
        Quaternion r = new Quaternion(a.x * s + bx * t, a.y * s + by * t, a.z * s + bz * t, a.w * s + bw * t);
        return Normalize(r);
    }

    /// <summary>Spherical interpolation along the shorter way round; t is clamped to 0..1.</summary>
    public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
    {
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;
        return SlerpUnclamped(a, b, t);
    }

    public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t)
    {
        float dot = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        float bx = b.x;
        float by = b.y;
        float bz = b.z;
        float bw = b.w;
        if (dot < 0f)
        {
            dot = -dot;
            bx = -bx;
            by = -by;
            bz = -bz;
            bw = -bw;
        }
        float k0;
        float k1;
        if (dot > 0.9995f)
        {
            k0 = 1f - t;
            k1 = t;
        }
        else
        {
            float theta = MathF.Acos(dot);
            float sinTheta = MathF.Sin(theta);
            k0 = MathF.Sin((1f - t) * theta) / sinTheta;
            k1 = MathF.Sin(t * theta) / sinTheta;
        }
        Quaternion r = new Quaternion(a.x * k0 + bx * k1, a.y * k0 + by * k1, a.z * k0 + bz * k1, a.w * k0 + bw * k1);
        return Normalize(r);
    }

    /// <summary>Turns from one rotation toward another by at most maxDegreesDelta degrees.</summary>
    public static Quaternion RotateTowards(Quaternion current, Quaternion target, float maxDegreesDelta)
    {
        float angle = Angle(current, target);
        if (angle == 0f) return target;
        float t = maxDegreesDelta / angle;
        if (t > 1f) t = 1f;
        return SlerpUnclamped(current, target, t);
    }

    /// <summary>The rotation that does lhs after rhs (Unity's <c>lhs * rhs</c>).</summary>
    public static Quaternion Multiply(Quaternion lhs, Quaternion rhs)
    {
        return new Quaternion(
            lhs.w * rhs.x + lhs.x * rhs.w + lhs.y * rhs.z - lhs.z * rhs.y,
            lhs.w * rhs.y + lhs.y * rhs.w + lhs.z * rhs.x - lhs.x * rhs.z,
            lhs.w * rhs.z + lhs.z * rhs.w + lhs.x * rhs.y - lhs.y * rhs.x,
            lhs.w * rhs.w - lhs.x * rhs.x - lhs.y * rhs.y - lhs.z * rhs.z);
    }

    /// <summary>A point turned by a rotation (Unity's <c>rotation * point</c>).</summary>
    public static Vector3 RotateVector(Quaternion rotation, Vector3 point)
    {
        float tx = 2f * (rotation.y * point.z - rotation.z * point.y);
        float ty = 2f * (rotation.z * point.x - rotation.x * point.z);
        float tz = 2f * (rotation.x * point.y - rotation.y * point.x);
        return new Vector3(
            point.x + rotation.w * tx + (rotation.y * tz - rotation.z * ty),
            point.y + rotation.w * ty + (rotation.z * tx - rotation.x * tz),
            point.z + rotation.w * tz + (rotation.x * ty - rotation.y * tx));
    }

#if !CRUST || UNITY_NATIVE_OPERATORS
    public static Quaternion operator *(Quaternion lhs, Quaternion rhs) { return Multiply(lhs, rhs); }
    public static Vector3 operator *(Quaternion rotation, Vector3 point) { return RotateVector(rotation, point); }
    public static bool operator ==(Quaternion lhs, Quaternion rhs)
    {
        return lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z + lhs.w * rhs.w > 0.999999f;
    }
    public static bool operator !=(Quaternion lhs, Quaternion rhs)
    {
        return !(lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z + lhs.w * rhs.w > 0.999999f);
    }
#endif

#if !CRUST
    public override bool Equals(object other) { return other is Quaternion q && this == q; }
    public override int GetHashCode() { return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1); }
    public override string ToString()
    {
        System.Globalization.CultureInfo c = System.Globalization.CultureInfo.InvariantCulture;
        return "(" + x.ToString("F1", c) + ", " + y.ToString("F1", c) + ", " + z.ToString("F1", c) + ", " + w.ToString("F1", c) + ")";
    }
#endif
}
