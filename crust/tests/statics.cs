using System;
class Cfg {
  public const int N = 4;
  public static int Total = 100;
  public static readonly int Scale = 3;
  public static int Bump(int by) { Total += by * Scale; return Total; }
}
class Prog {
  public static int Main() {
    Console.WriteLine(Cfg.N);
    Console.WriteLine(Cfg.Bump(2));
    Console.WriteLine(Cfg.Total);
    int[] a = new int[Cfg.N];
    Console.WriteLine(a.Length);
    return Cfg.Scale;
  }
}
