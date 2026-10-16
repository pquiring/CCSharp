using System;
// C# % on float / double is the truncated remainder (the sign follows the dividend). C has no such operator: it is lowered to fmod.
class Prog {
  static float Wrap(float a) {
    float twoPi = 6.2831855f;
    a %= twoPi;
    if (a > 3.1415927f) a -= twoPi;
    else if (a <= -3.1415927f) a += twoPi;
    return a;
  }
  public static int Main() {
    Console.WriteLine((int)(7.5 % 2.0 * 100.0));
    Console.WriteLine((int)(-7.5 % 2.0 * 100.0));
    Console.WriteLine((int)(7.5 % -2.0 * 100.0));
    Console.WriteLine((int)(7.5f % 2f * 100f));
    Console.WriteLine((int)(-7.5f % 2f * 100f));
    double d = 10.25;
    d %= 3.0;
    Console.WriteLine((int)(d * 100.0));
    float f = -10.25f;
    f %= 3f;
    Console.WriteLine((int)(f * 100f));
    float[] a = new float[2];
    a[1] = 9.5f;
    a[1] %= 4f;
    Console.WriteLine((int)(a[1] * 100f));
    Console.WriteLine((int)(Wrap(9.42477796f) * 1000f));    // 3pi -> pi
    Console.WriteLine((int)(Wrap(-9.42477796f) * 1000f));
    Console.WriteLine((int)(Wrap(0.5f) * 1000f));
    Console.WriteLine(17 % 5);                                 // integer % is untouched
    return 0;
  }
}
