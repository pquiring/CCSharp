using System;
// float arithmetic and comparison work in Crust; printing a float does not, so every result is an int or a bool.
class Prog {
  static bool Near(float a, float b) { return MathF.Abs(a - b) < 0.0001f; }
  public static int Main() {
    Console.WriteLine((int)MathF.Sqrt(144f));
    Console.WriteLine((int)(MathF.PI * 1000f));
    Console.WriteLine((int)(MathF.Atan2(1f, 1f) * 1000f));        // pi/4
    Console.WriteLine((int)(MathF.Atan2(-1f, -1f) * 1000f));      // -3pi/4
    Console.WriteLine((int)(MathF.Atan(1f) * 1000f));
    Console.WriteLine(Near(MathF.Sin(MathF.PI / 2f), 1f));
    Console.WriteLine(Near(MathF.Cos(MathF.PI), -1f));
    Console.WriteLine(Near(MathF.Tan(MathF.PI / 4f), 1f));
    Console.WriteLine(Near(MathF.Asin(1f), MathF.PI / 2f));
    Console.WriteLine(Near(MathF.Acos(1f), 0f));
    Console.WriteLine((int)MathF.Floor(-2.5f));
    Console.WriteLine((int)MathF.Ceiling(-2.5f));
    Console.WriteLine((int)MathF.Pow(2f, 10f));
    Console.WriteLine(Near(MathF.Exp(0f), 1f));
    Console.WriteLine(Near(MathF.Log(MathF.E), 1f));
    Console.WriteLine(MathF.Max(1.5f, 2.5f) > 2.4f);
    Console.WriteLine(MathF.Min(1.5f, 2.5f) < 1.6f);
    Console.WriteLine((int)MathF.Abs(-7.5f));
    // Math.Clamp, all four overloads, below / inside / above
    Console.WriteLine(Math.Clamp(-5, 0, 10));
    Console.WriteLine(Math.Clamp(5, 0, 10));
    Console.WriteLine(Math.Clamp(50, 0, 10));
    Console.WriteLine(Math.Clamp(5000000000L, 0L, 7000000000L));
    Console.WriteLine(Math.Clamp(-9000000000L, -1L, 1L));
    Console.WriteLine((int)Math.Clamp(1.5f, 0f, 1f));
    Console.WriteLine((int)(Math.Clamp(-0.5f, 0f, 1f) * 10f));
    Console.WriteLine((int)Math.Clamp(2.5, 0.0, 10.0));
    Console.WriteLine((int)(Math.Clamp(0.25, 0.5, 1.0) * 100.0));
    // the double functions that were declared but unimplemented
    Console.WriteLine((int)(Math.Atan2(1.0, 1.0) * 1000.0));
    Console.WriteLine((int)(Math.Tan(0.5) * 1000.0));
    Console.WriteLine((int)(Math.Exp(1.0) * 1000.0));
    Console.WriteLine((int)(Math.Log(10.0) * 1000.0));
    Console.WriteLine((int)(Math.Asin(0.5) * 1000.0));
    Console.WriteLine((int)(Math.Acos(0.5) * 1000.0));
    Console.WriteLine((int)(Math.Atan(1.0) * 1000.0));
    return 0;
  }
}
