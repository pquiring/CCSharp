using System;
// the arguments of one call: a counter call, then a string built from another counter call
class Prog {
  static int n = 0;
  static int Next() { n++; return n; }
  static string Tag(string s) { n += 10; return s + n; }
  static void Show(int a, string b, int c) { Console.WriteLine(a + "|" + b + "|" + c); }
  static string Pair(string a, string b) { return a + "&" + b; }
  public static int Main() {
    Show(Next(), "n=" + Next(), Next());      // 1|n=2|3
    Show(n, "n=" + Next(), n);                // reads n before and after the call: 3|n=4|4
    Console.WriteLine(Pair(Tag("a"), Tag("b")));
    Console.WriteLine(Pair(Tag("c"), "x" + Next()) + "!");
    Console.WriteLine(Next() + Next() + Next() + Next());   // a left-assoc chain of four calls
    return n;
  }
}
