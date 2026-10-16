// dna
using System;
class ManagedAttribute : Attribute { }
class Kernel {
  public static int Sum(int[] a) { int t = 0; for (int i = 0; i < a.Length; i++) t += a[i]; return t; }
}
// the managed code allocates enough that the collector runs while strings and arrays from native code are in its hands
[Managed]
class Alloc {
  public static int Work(string s, int[] a, int rounds) {
    int sink = 0;
    for (int i = 0; i < rounds; i++) {
      int[] garbage = new int[100];
      garbage[0] = i;
      string junk = "x" + i;
      sink += garbage[0] + junk.Length;
    }
    return s.Length * 1000 + Kernel.Sum(a) + (sink & 3);
  }
  public static string Build(int n) { string r = ""; for (int i = 0; i < n; i++) r += i; return r; }
  public static int[] Range(int n) { int[] r = new int[n]; for (int i = 0; i < n; i++) r[i] = i * 7; return r; }
}
class Program {
  static int Main() {
    int[] a = new int[4]; a[0] = 1; a[1] = 2; a[2] = 3; a[3] = 4;
    int total = 0;
    for (int i = 0; i < 150; i++) {
      if (i % 3 == 0) total += Alloc.Work("alpha", a, 400);
      else if (i % 3 == 1) total += Alloc.Work("a rather longer string", a, 400);
      else total += Alloc.Work("", a, 400);
    }
    Console.WriteLine(total);
    Console.WriteLine(Alloc.Build(15));
    int[] r = Alloc.Range(12);
    Console.WriteLine(Kernel.Sum(r));
    Console.WriteLine(r[11]);
    return 0;
  }
}
