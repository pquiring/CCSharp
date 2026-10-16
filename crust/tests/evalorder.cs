using System;
// C# evaluates call arguments and binary operands strictly left to right; C does not specify an order.
class Prog {
  static int n = 0;
  static int Next() { n++; return n; }
  static int Sub(int a, int b) { return a * 10 + b; }
  static int Three(int a, int b, int c) { return a * 100 + b * 10 + c; }
  public static int Main() {
    Console.WriteLine(Sub(Next(), Next()));                 // 12
    Console.WriteLine(Three(Next(), Next(), Next()));       // 345
    int x = Next() - Next();                                // 6 - 7
    Console.WriteLine(x);
    int y = Next() * 10 + Next();                           // 8*10 + 9
    Console.WriteLine(y);
    Console.WriteLine(Math.Max(Next(), Next() - 20));       // max(10, -9)
    Console.WriteLine("abcdefghij".Substring(Next() - 9, Next() - 9));   // Substring(2, 3)... see below
    n = 0;
    Console.WriteLine(Sub(Next(), Next()) + Sub(Next(), Next()));        // 12 + 34
    int a = 5;
    Console.WriteLine(Sub(a, a++) );                        // 55: the first argument is read before a++
    return n;
  }
}
