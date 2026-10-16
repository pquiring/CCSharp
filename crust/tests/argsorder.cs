using System;
class Prog {
  static int n = 0;
  static int Next() { n++; return n; }
  static string Tag(string s) { n += 10; return s + n; }
  public static int Main(string[] args) {
    // argument evaluation order is left to right in C#, unspecified in C
    Console.WriteLine($"{Next()} {Next()} {Next()}");
    Console.WriteLine(Next() + " then " + Next());
    int x = 5;
    Console.WriteLine($"{x} {Bump(ref x)} {x}");     // reads x before and after the call
    Console.WriteLine(Tag("a") + "|" + Tag("b"));
    Console.WriteLine(args.Length);
    return n;
  }
  static int Bump(ref int v) { v += 100; return v; }
}
