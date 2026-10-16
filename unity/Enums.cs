// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

// CCSharp --unity-types: bundled UnityEngine types. Same source as Prowl2D's Prowl.Core2D/Unity/Enums.cs, but with UNITY_NATIVE_OPERATORS defined,
// because CCSharp lowers operators on a struct. Keep the two in step (a change is made in both).

namespace UnityEngine;

/// <summary>How <c>Rigidbody2D.AddForce</c> applies a force (UnityEngine.ForceMode2D): Force is a push over time, Impulse an instant change of momentum.</summary>
public enum ForceMode2D
{
    Force = 0,
    Impulse = 1
}

/// <summary>What a body does (UnityEngine.RigidbodyType2D). Note that the numbers are Unity's, not the native shim's (<c>PB2.BodyStatic</c> and the others).</summary>
public enum RigidbodyType2D
{
    Dynamic = 0,
    Kinematic = 1,
    Static = 2
}

/// <summary>The space a move or turn is measured in (UnityEngine.Space).</summary>
public enum Space
{
    World = 0,
    Self = 1
}
