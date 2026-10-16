// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

// CCSharp --unity-types: bundled UnityEngine types. Same source as Prowl2D's Prowl.Core2D/Unity/Color.cs, but with UNITY_NATIVE_OPERATORS defined,
// because CCSharp lowers operators on a struct. Keep the two in step (a change is made in both).

// See the note at the top of Vector2.cs: uncomment to give the C build the operators, once `python3 build.py scan` shows its CCSharp accepts them.
#define UNITY_NATIVE_OPERATORS

using System;

namespace UnityEngine;

/// <summary>
/// UnityEngine.Color: red, green, blue and alpha, each 0..1. The operators are .NET only until <c>UNITY_NATIVE_OPERATORS</c> is defined; by name they
/// are <see cref="Add"/>, <see cref="Subtract"/>, <see cref="Multiply"/> (by a colour) and <see cref="Scale"/> (by a number).
/// </summary>
public struct Color
{
    public float r;
    public float g;
    public float b;
    public float a;

    public Color(float pr, float pg, float pb, float pa)
    {
        r = pr;
        g = pg;
        b = pb;
        a = pa;
    }

    /// <summary>As in Unity, alpha is 1.</summary>
    public Color(float pr, float pg, float pb)
    {
        r = pr;
        g = pg;
        b = pb;
        a = 1f;
    }

    public static Color red { get { return new Color(1f, 0f, 0f, 1f); } }
    public static Color green { get { return new Color(0f, 1f, 0f, 1f); } }
    public static Color blue { get { return new Color(0f, 0f, 1f, 1f); } }
    public static Color white { get { return new Color(1f, 1f, 1f, 1f); } }
    public static Color black { get { return new Color(0f, 0f, 0f, 1f); } }
    public static Color yellow { get { return new Color(1f, 0.92156863f, 0.015686275f, 1f); } }
    public static Color cyan { get { return new Color(0f, 1f, 1f, 1f); } }
    public static Color magenta { get { return new Color(1f, 0f, 1f, 1f); } }
    public static Color gray { get { return new Color(0.5f, 0.5f, 0.5f, 1f); } }
    public static Color grey { get { return new Color(0.5f, 0.5f, 0.5f, 1f); } }
    public static Color clear { get { return new Color(0f, 0f, 0f, 0f); } }

    /// <summary>The colour's brightness, 0..1.</summary>
    public float grayscale { get { return 0.299f * r + 0.587f * g + 0.114f * b; } }

    public float maxColorComponent { get { return MathF.Max(MathF.Max(r, g), b); } }

    /// <summary>Linear interpolation between two colours; t is clamped to 0..1.</summary>
    public static Color Lerp(Color p, Color q, float t)
    {
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;
        return new Color(p.r + (q.r - p.r) * t, p.g + (q.g - p.g) * t, p.b + (q.b - p.b) * t, p.a + (q.a - p.a) * t);
    }

    public static Color LerpUnclamped(Color p, Color q, float t)
    {
        return new Color(p.r + (q.r - p.r) * t, p.g + (q.g - p.g) * t, p.b + (q.b - p.b) * t, p.a + (q.a - p.a) * t);
    }

    // ---- the operators, by name (for the C build until UNITY_NATIVE_OPERATORS) --------------

    public static Color Add(Color p, Color q) { return new Color(p.r + q.r, p.g + q.g, p.b + q.b, p.a + q.a); }
    public static Color Subtract(Color p, Color q) { return new Color(p.r - q.r, p.g - q.g, p.b - q.b, p.a - q.a); }
    public static Color Multiply(Color p, Color q) { return new Color(p.r * q.r, p.g * q.g, p.b * q.b, p.a * q.a); }
    public static Color Scale(Color p, float f) { return new Color(p.r * f, p.g * f, p.b * f, p.a * f); }

#if !CRUST || UNITY_NATIVE_OPERATORS
    public static Color operator +(Color p, Color q) { return new Color(p.r + q.r, p.g + q.g, p.b + q.b, p.a + q.a); }
    public static Color operator -(Color p, Color q) { return new Color(p.r - q.r, p.g - q.g, p.b - q.b, p.a - q.a); }
    public static Color operator *(Color p, Color q) { return new Color(p.r * q.r, p.g * q.g, p.b * q.b, p.a * q.a); }
    public static Color operator *(Color p, float f) { return new Color(p.r * f, p.g * f, p.b * f, p.a * f); }
    public static Color operator *(float f, Color p) { return new Color(p.r * f, p.g * f, p.b * f, p.a * f); }
    public static Color operator /(Color p, float f) { return new Color(p.r / f, p.g / f, p.b / f, p.a / f); }
    public static bool operator ==(Color p, Color q)
    {
        return p.r == q.r && p.g == q.g && p.b == q.b && p.a == q.a;
    }
    public static bool operator !=(Color p, Color q)
    {
        return !(p.r == q.r && p.g == q.g && p.b == q.b && p.a == q.a);
    }
#endif

#if !CRUST
    public override bool Equals(object other) { return other is Color c && this == c; }
    public override int GetHashCode() { return r.GetHashCode() ^ (g.GetHashCode() << 2) ^ (b.GetHashCode() >> 2) ^ (a.GetHashCode() >> 1); }
    public override string ToString()
    {
        System.Globalization.CultureInfo c = System.Globalization.CultureInfo.InvariantCulture;
        return "RGBA(" + r.ToString("F3", c) + ", " + g.ToString("F3", c) + ", " + b.ToString("F3", c) + ", " + a.ToString("F3", c) + ")";
    }
#endif
}
