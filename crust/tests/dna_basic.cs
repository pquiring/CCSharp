// dna
using System;
class ManagedAttribute : Attribute { }

// native: lowered to C
class Vec {
  public static int Twice(int a) { return a * 2; }
  public static int Sum(int[] a) { int t = 0; for (int i = 0; i < a.Length; i++) t += a[i]; return t; }
  public static string Shout(string s) { return s + "!"; }
}

// managed: run by DotNetAnywhere
[Managed]
class Script {
  public static int Go(int n) { return Vec.Twice(n) + 1; }
  public static string Name(string s) { return "<" + Vec.Shout(s) + ">"; }
  public static int SumSq(int[] a) { int t = 0; for (int i = 0; i < a.Length; i++) t += a[i] * a[i]; return t; }
  public static int[] Squares(int n) { int[] r = new int[n]; for (int i = 0; i < n; i++) r[i] = i * i; return r; }
}

class Program {
  static int Main() {
    Console.WriteLine(Script.Go(5));
    Console.WriteLine(Script.Name("hi"));
    int[] xs = new int[3]; xs[0] = 1; xs[1] = 2; xs[2] = 3;
    Console.WriteLine(Script.SumSq(xs));
    int[] sq = Script.Squares(5);
    Console.WriteLine(Vec.Sum(sq));
    return 0;
  }
}
