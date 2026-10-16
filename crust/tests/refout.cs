using System;
class Prog {
  static void Swap(ref int a, ref int b) { int t = a; a = b; b = t; }
  static bool Div(int a, int b, out int q) { if (b == 0) { q = 0; return false; } q = a / b; return true; }
  public static int Main() {
    int x = 1, y = 2;
    Swap(ref x, ref y);
    Console.WriteLine(x * 10 + y);
    int q;
    bool ok = Div(17, 5, out q);
    Console.WriteLine(ok);
    Console.WriteLine(q);
    ok = Div(1, 0, out q);
    Console.WriteLine(ok);
    return q;
  }
}
