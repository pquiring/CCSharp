using System;
using System.Collections.Generic;
class Prog {
  static int Sum(int[] a) { int s = 0; foreach (int v in a) s += v; return s; }
  static void Fill(int[] a) { for (int i = 0; i < a.Length; i++) a[i] = i * i; }
  public static int Main() {
    int[] a = new int[6];
    Fill(a);
    Console.WriteLine(Sum(a));
    Console.WriteLine(a.Length);
    Console.WriteLine(a[5]);
    List<int> xs = new List<int>();
    for (int i = 1; i <= 5; i++) xs.Add(i * 10);
    Console.WriteLine(xs.Count);
    int t = 0;
    foreach (var x in xs) t += x;
    Console.WriteLine(t);
    xs[0] = 7;
    Console.WriteLine(xs[0] + xs[4]);
    byte[] bs = new byte[3];
    bs[1] = 200; bs[1] += 100;
    Console.WriteLine(bs[1]);
    bool[] flags = new bool[2];
    flags[1] = true;
    Console.WriteLine(flags[0]);
    Console.WriteLine(flags[1]);
    return 0;
  }
}
