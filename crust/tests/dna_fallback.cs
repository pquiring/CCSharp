// dna
using System;
// (no [Managed] anywhere: Crust refuses `try`/`catch` and lambdas, so these classes fall back to the managed side on their own)
class Safe {
  public static int ParseOr(string s, int dflt) {
    try { return int.Parse(s); } catch (Exception) { return dflt; }
  }
  public static int Apply(int n) { Func<int, int> f = k => k * k + n; return f(3); }
  public static string Describe(int n) {
    try {
      if (n < 0) throw new ArgumentException("negative");
      return "ok " + n;
    } catch (ArgumentException) { return "bad"; }
  }
}

class Kernel {
  public static int Fib(int n) { int a = 0, b = 1; for (int i = 0; i < n; i++) { int t = a + b; a = b; b = t; } return a; }
}

class Program {
  static int Main() {
    Console.WriteLine(Safe.ParseOr("42", 7));
    Console.WriteLine(Safe.ParseOr("x", 7));
    Console.WriteLine(Safe.Apply(1));
    Console.WriteLine(Safe.Describe(5));
    Console.WriteLine(Safe.Describe(-5));
    Console.WriteLine(Kernel.Fib(20));
    return 0;
  }
}
