// dna
using System;
class ManagedAttribute : Attribute { }
// a static field keeps its value from call to call, on either side
class Native {
  static int ticks;
  public static int Tick() { ticks = ticks + 1; return ticks; }
}
[Managed]
class Counter {
  static int n;
  static string last = "";
  public static int Next() { n++; return n; }
  public static int Both() { return Next() * 100 + Native.Tick(); }
  public static string Remember(string s) { string old = last; last = s; return old + ">" + s; }
}
class Program {
  static int Main() {
    for (int i = 0; i < 3; i++) Console.WriteLine(Counter.Both());
    Console.WriteLine(Native.Tick());
    Console.WriteLine(Counter.Next());
    Console.WriteLine(Counter.Remember("a"));
    Console.WriteLine(Counter.Remember("b"));
    return 0;
  }
}
