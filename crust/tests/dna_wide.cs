// dna
using System;
class ManagedAttribute : Attribute { }

// More than six C arguments across the boundary, in both directions: past the six integer and eight floating-point registers a C call has the rest on the
// stack. The same signatures on both sides.
class N {
  public static int Sum9(int a, int b, int c, int d, int e, int f, int g, int h, int i) { return a + 2 * b + 3 * c + 4 * d + 5 * e + 6 * f + 7 * g + 8 * h + 9 * i; }
  public static long Mix8(long a, int b, long c, int d, long e, int f, long g, int h) { return a - b + 2 * c - d + 3 * e - f + 4 * g - h; }
  public static double Dbl10(double a, double b, double c, double d, double e, double f, double g, double h, double i, double j) { return a + 2 * b + 3 * c + 4 * d + 5 * e + 6 * f + 7 * g + 8 * h + 9 * i + 10 * j; }
  public static double Mixed(int a, double b, long c, float d, int e, double f, int g, long h, float i, int j) { return a + b * 2 + c * 3 + d * 4 + e * 5 + f * 6 + g * 7 + h * 8 + i * 9 + j * 10; }
  public static int Arr(int[] p, int a, int b, int c, int d, int e, int f) { int s = 0; for (int k = 0; k < p.Length; k++) s += p[k]; return s + a + b + c + d + e + f; }
  public static string Str(string s, int a, int b, int c, int d, int e, int f) { return s + (a + b + c + d + e + f); }
}

[Managed]
class M {
  public static int Sum9(int a, int b, int c, int d, int e, int f, int g, int h, int i) { return a + 2 * b + 3 * c + 4 * d + 5 * e + 6 * f + 7 * g + 8 * h + 9 * i; }
  public static long Mix8(long a, int b, long c, int d, long e, int f, long g, int h) { return a - b + 2 * c - d + 3 * e - f + 4 * g - h; }
  public static double Dbl10(double a, double b, double c, double d, double e, double f, double g, double h, double i, double j) { return a + 2 * b + 3 * c + 4 * d + 5 * e + 6 * f + 7 * g + 8 * h + 9 * i + 10 * j; }
  public static double Mixed(int a, double b, long c, float d, int e, double f, int g, long h, float i, int j) { return a + b * 2 + c * 3 + d * 4 + e * 5 + f * 6 + g * 7 + h * 8 + i * 9 + j * 10; }
}

[Managed]
class Driver {
  public static void Run() {
    Console.WriteLine("native, called from managed");
    Console.WriteLine(N.Sum9(1, 2, 3, 4, 5, 6, 7, 8, 9));
    Console.WriteLine(N.Sum9(-1, 20, -3, 40, -5, 60, -7, 80, -9));
    Console.WriteLine(N.Mix8(1L << 40, 2, 3L << 33, 4, -5, 6, 7L << 35, 8));
    Console.WriteLine(N.Dbl10(1, 2, 3, 4, 5, 6, 7, 8, 9, 10));
    Console.WriteLine(N.Mixed(1, 2.5, 3, 4.5f, 5, 6.25, 7, 8, 9.5f, 10));
    Console.WriteLine(N.Arr(new int[] { 1, 2, 3 }, 10, 20, 30, 40, 50, 60));
    Console.WriteLine(N.Str("n=", 1, 2, 3, 4, 5, 6));
    Console.WriteLine("managed, called from managed");
    Console.WriteLine(M.Sum9(1, 2, 3, 4, 5, 6, 7, 8, 9));
    Console.WriteLine(M.Mix8(1L << 40, 2, 3L << 33, 4, -5, 6, 7L << 35, 8));
    Console.WriteLine(M.Dbl10(1, 2, 3, 4, 5, 6, 7, 8, 9, 10));
    Console.WriteLine(M.Mixed(1, 2.5, 3, 4.5f, 5, 6.25, 7, 8, 9.5f, 10));
  }
}

// native code calls the managed M
class Native {
  public static void Run() {
    Console.WriteLine("managed, called from native");
    Console.WriteLine(M.Sum9(9, 8, 7, 6, 5, 4, 3, 2, 1));
    Console.WriteLine(M.Mix8(5, 4, 3, 2, 1, 0, -1, -2));
    Console.WriteLine(M.Dbl10(0.5, 1.5, 2.5, 3.5, 4.5, 5.5, 6.5, 7.5, 8.5, 9.5));
    Console.WriteLine(M.Mixed(10, 9.5, 8, 7.5f, 6, 5.25, 4, 3, 2.5f, 1));
  }
}

class Program {
  static void Main() {
    Native.Run();
    Driver.Run();
  }
}
