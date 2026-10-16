using System;
using System.Diagnostics;
class Prog {
  public static int Main() {
    Stopwatch sw = Stopwatch.StartNew();
    long a = Environment.TickCount64;
    int spin = 0;
    for (int i = 0; i < 1000000; i++) spin += i % 7;
    long ms = sw.ElapsedMilliseconds;
    Console.WriteLine(ms >= 0);
    Console.WriteLine(ms < 5000);
    Console.WriteLine(Environment.TickCount64 >= a);
    sw.Restart();
    Console.WriteLine(sw.ElapsedMilliseconds < 1000);
    Console.WriteLine(spin > 0);
    Console.WriteLine("a" + Environment.NewLine + "b");
    return 0;
  }
}
