using System;
// Math.Min / Max / Abs have float overloads in .NET. Without them `Math.Min(floatA, floatB)` bound to the double overload and
// failed with CS0266 (double -> float). Every result is an int: a float cannot be printed in Crust.
class Prog {
  static float Half(float v) { return Math.Min(v, 0.5f); }
  static float Floor0(float v) { return Math.Max(v, 0f); }
  public static int Main() {
    float a = 1.25f, b = -2.5f;
    float m = Math.Min(a, b);          // float, not double: assigns without a cast
    float x = Math.Max(a, b);
    float y = Math.Abs(b);
    Console.WriteLine((int)(m * 100f));
    Console.WriteLine((int)(x * 100f));
    Console.WriteLine((int)(y * 100f));
    Console.WriteLine((int)(Half(3f) * 100f));
    Console.WriteLine((int)(Floor0(-4f) * 100f));
    Console.WriteLine(Math.Min(7, 3));
    Console.WriteLine((int)Math.Max(2.5, 1.5));
    return 0;
  }
}
