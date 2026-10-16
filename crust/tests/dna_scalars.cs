// dna
using System;
class ManagedAttribute : Attribute { }

// the same twelve signatures on both sides, so each kind crosses in both directions
class N {
  public static bool Not(bool b) { return !b; }
  public static byte AddB(byte a, byte b) { return (byte)(a + b); }
  public static sbyte NegS(sbyte a) { return (sbyte)(-a); }
  public static short MulS(short a, short b) { return (short)(a * b); }
  public static ushort IncU(ushort a) { return (ushort)(a + 1); }
  public static int AddI(int a, int b) { return a + b; }
  public static uint MulU(uint a, uint b) { return a * b; }
  public static long MulL(long a, long b) { return a * b; }
  public static ulong Shl(ulong a, int n) { return a << n; }
  public static float HalfF(float a) { return a * 0.5f; }
  public static double HypD(double a, double b) { return Math.Sqrt(a * a + b * b); }
  public static int Mix(int a, double b, long c, float d, int e) { return a + (int)b + (int)c + (int)d + e; }
  public static void Nop() { }
}

[Managed]
class M {
  public static bool Not(bool b) { return !b; }
  public static byte AddB(byte a, byte b) { return (byte)(a + b); }
  public static sbyte NegS(sbyte a) { return (sbyte)(-a); }
  public static short MulS(short a, short b) { return (short)(a * b); }
  public static ushort IncU(ushort a) { return (ushort)(a + 1); }
  public static int AddI(int a, int b) { return a + b; }
  public static uint MulU(uint a, uint b) { return a * b; }
  public static long MulL(long a, long b) { return a * b; }
  public static ulong Shl(ulong a, int n) { return a << n; }
  public static float HalfF(float a) { return a * 0.5f; }
  public static double HypD(double a, double b) { return Math.Sqrt(a * a + b * b); }
  public static int Mix(int a, double b, long c, float d, int e) { return a + (int)b + (int)c + (int)d + e; }
  public static void Nop() { }
}

// managed code calls the native N
[Managed]
class Driver {
  public static void Run() {
    Console.WriteLine("native, called from managed");
    Console.WriteLine(N.Not(true) ? 1 : 0);
    Console.WriteLine(N.Not(false) ? 1 : 0);
    Console.WriteLine(N.AddB(200, 100));
    Console.WriteLine(N.NegS(-100));
    Console.WriteLine(N.MulS(300, 300));
    Console.WriteLine(N.IncU(65535));
    Console.WriteLine(N.AddI(-5, 3));
    Console.WriteLine((long)N.MulU(4000000000u, 3u));
    Console.WriteLine(N.MulL(3000000000L, 7L));
    Console.WriteLine((long)N.Shl(3UL, 40));
    Console.WriteLine((int)(N.HalfF(5f) * 1000f));
    Console.WriteLine((long)(N.HypD(3.0, 4.0) * 1000.0));
    Console.WriteLine(N.Mix(1, 2.9, 3L, 4.9f, 5));
    N.Nop();
  }
}

class Program {
  static int Main() {
    Console.WriteLine("managed, called from native");
    Console.WriteLine(M.Not(true) ? 1 : 0);
    Console.WriteLine(M.Not(false) ? 1 : 0);
    Console.WriteLine(M.AddB(200, 100));
    Console.WriteLine(M.NegS(-100));
    Console.WriteLine(M.MulS(300, 300));
    Console.WriteLine(M.IncU(65535));
    Console.WriteLine(M.AddI(-5, 3));
    Console.WriteLine((long)M.MulU(4000000000u, 3u));
    Console.WriteLine(M.MulL(3000000000L, 7L));
    Console.WriteLine((long)M.Shl(3UL, 40));
    Console.WriteLine((int)(M.HalfF(5f) * 1000f));
    Console.WriteLine((long)(M.HypD(3.0, 4.0) * 1000.0));
    Console.WriteLine(M.Mix(1, 2.9, 3L, 4.9f, 5));
    M.Nop();
    Driver.Run();
    return 0;
  }
}
