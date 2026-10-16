// dna
using System;
class ManagedAttribute : Attribute { }
// arrays cross by copy, and come back: what the callee wrote is seen by the caller, on either side
class N {
  public static void FillI(int[] a, int v) { for (int i = 0; i < a.Length; i++) a[i] = v + i; }
  public static long SumL(long[] a) { long t = 0; for (int i = 0; i < a.Length; i++) t += a[i]; return t; }
  public static void Upper(byte[] a) { for (int i = 0; i < a.Length; i++) if (a[i] >= 97 && a[i] <= 122) a[i] = (byte)(a[i] - 32); }
  public static double Dot(double[] a, double[] b) { double t = 0; for (int i = 0; i < a.Length; i++) t += a[i] * b[i]; return t; }
  public static int Len(short[] a) { return a.Length; }
  public static void Halve(float[] a) { for (int i = 0; i < a.Length; i++) a[i] = a[i] * 0.5f; }
}
[Managed]
class M {
  public static void FillI(int[] a, int v) { for (int i = 0; i < a.Length; i++) a[i] = v + i; }
  public static long SumL(long[] a) { long t = 0; for (int i = 0; i < a.Length; i++) t += a[i]; return t; }
  public static void Upper(byte[] a) { for (int i = 0; i < a.Length; i++) if (a[i] >= 97 && a[i] <= 122) a[i] = (byte)(a[i] - 32); }
  public static double Dot(double[] a, double[] b) { double t = 0; for (int i = 0; i < a.Length; i++) t += a[i] * b[i]; return t; }
  public static int Len(short[] a) { return a.Length; }
  public static void Halve(float[] a) { for (int i = 0; i < a.Length; i++) a[i] = a[i] * 0.5f; }
  // managed code hands its own arrays to native code
  public static void Run() {
    int[] f = new int[5]; N.FillI(f, 10);
    Console.WriteLine(f[0] + " " + f[1] + " " + f[4]);
    long[] l = new long[3]; l[0] = 4000000000L; l[1] = 5000000000L; l[2] = -1;
    Console.WriteLine(N.SumL(l));
    byte[] b = new byte[5]; b[0] = 104; b[1] = 105; b[2] = 33; b[3] = 97; b[4] = 98;
    N.Upper(b);
    Console.WriteLine(b[0] + " " + b[1] + " " + b[2] + " " + b[3] + " " + b[4]);
    double[] x = new double[3]; x[0] = 1; x[1] = 2; x[2] = 3;
    double[] y = new double[3]; y[0] = 4; y[1] = 5; y[2] = 6;
    Console.WriteLine((long)(N.Dot(x, y) * 1000.0));
    short[] s7 = new short[7]; short[] s0 = new short[0];
    Console.WriteLine(N.Len(s7));
    Console.WriteLine(N.Len(s0));
    float[] h = new float[3]; h[0] = 1; h[1] = 3; h[2] = 5;
    N.Halve(h);
    Console.WriteLine((int)(h[0] * 10f) + " " + (int)(h[1] * 10f) + " " + (int)(h[2] * 10f));
  }
}
class Program {
  static int Main() {
    int[] f = new int[5]; M.FillI(f, 10);
    Console.WriteLine(f[0] + " " + f[1] + " " + f[4]);
    long[] l = new long[3]; l[0] = 4000000000L; l[1] = 5000000000L; l[2] = -1;
    Console.WriteLine(M.SumL(l));
    byte[] b = new byte[5]; b[0] = 104; b[1] = 105; b[2] = 33; b[3] = 97; b[4] = 98;
    M.Upper(b);
    Console.WriteLine(b[0] + " " + b[1] + " " + b[2] + " " + b[3] + " " + b[4]);
    double[] x = new double[3]; x[0] = 1; x[1] = 2; x[2] = 3;
    double[] y = new double[3]; y[0] = 4; y[1] = 5; y[2] = 6;
    Console.WriteLine((long)(M.Dot(x, y) * 1000.0));
    short[] s7 = new short[7]; short[] s0 = new short[0];
    Console.WriteLine(M.Len(s7));
    Console.WriteLine(M.Len(s0));
    float[] h = new float[3]; h[0] = 1; h[1] = 3; h[2] = 5;
    M.Halve(h);
    Console.WriteLine((int)(h[0] * 10f) + " " + (int)(h[1] * 10f) + " " + (int)(h[2] * 10f));
    M.Run();
    return 0;
  }
}
