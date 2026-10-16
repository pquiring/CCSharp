using System;
using System.Collections.Generic;
class Prog {
  public static int Main() {
    Dictionary<int, int> d = new Dictionary<int, int>();
    d[1] = 10;
    d[7] = 70;
    d[3] = 30;
    Console.WriteLine(d.Count);
    Console.WriteLine(d[7]);
    Console.WriteLine(d.ContainsKey(3));
    Console.WriteLine(d.ContainsKey(4));
    d[3] += 5;
    Console.WriteLine(d[3]);
    Console.WriteLine(d.Remove(1));
    Console.WriteLine(d.Remove(1));
    Console.WriteLine(d.Count);
    d.Clear();
    Console.WriteLine(d.Count);
    return d.Count;
  }
}
