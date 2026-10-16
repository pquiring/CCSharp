// dna
using System;
class ManagedAttribute : Attribute { }
// native -> managed -> native -> managed ... ten levels deep
class Ping {
  public static int Depth(int n) { if (n <= 0) return 0; return 1 + Pong.Depth(n - 1); }
  public static int Weigh(int n, int acc) { if (n <= 0) return acc; return Pong.Weigh(n - 1, acc * 2 + n); }
}
[Managed]
class Pong {
  public static int Depth(int n) { if (n <= 0) return 0; return 1 + Ping.Depth(n - 1); }
  public static int Weigh(int n, int acc) { if (n <= 0) return acc; return Ping.Weigh(n - 1, acc * 3 + n); }
}
class Program {
  static int Main() {
    Console.WriteLine(Ping.Depth(10));
    Console.WriteLine(Pong.Depth(10));
    Console.WriteLine(Ping.Depth(0));
    Console.WriteLine(Ping.Weigh(9, 1));
    Console.WriteLine(Pong.Weigh(9, 1));
    return 0;
  }
}
