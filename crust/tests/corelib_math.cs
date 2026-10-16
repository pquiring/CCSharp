using System;
class Prog {
  public static int Main() {
    Console.WriteLine(Math.Max(3, 9));
    Console.WriteLine(Math.Min(3, 9));
    Console.WriteLine(Math.Max(5000000000L, 7L));
    Console.WriteLine(Math.Min(-5000000000L, 7L));
    Console.WriteLine(Math.Abs(-12));
    Console.WriteLine(Math.Abs(12));
    Console.WriteLine(Math.Abs(-5000000000L));
    Console.WriteLine(Math.Sign(-9));
    Console.WriteLine(Math.Sign(0));
    Console.WriteLine(Math.Sign(4));
    Console.WriteLine((int)Math.Sqrt(144.0));
    Console.WriteLine((int)Math.Pow(2.0, 10.0));
    Console.WriteLine((int)Math.Floor(3.7));
    Console.WriteLine((int)Math.Ceiling(3.2));
    Console.WriteLine((int)(Math.PI * 100.0));
    Console.WriteLine((int)Math.Max(2.5, 1.5));
    Console.WriteLine(Math.Abs(-2.5) > 2.4);
    int x = Math.Max(Math.Min(15, 10), Math.Abs(-3));
    Console.WriteLine(x);
    return x;
  }
}
