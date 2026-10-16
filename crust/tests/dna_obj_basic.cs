// dna
using System;
class ManagedAttribute : Attribute { }

// Native code holds objects of managed classes: construct, call, read and write properties, pass them to managed methods.
[Managed]
class Counter {
  int n;
  string label = "";
  public int Count { get { return n; } set { n = value; } }
  public string Label { get { return label; } set { label = value; } }
  public bool Positive { get { return n > 0; } }
  public double Half { get { return n * 0.5; } }
  public Counter(int start) { n = start; }
  public int Add(int k) { n += k; return n; }
  public long Big(long x) { return x * n; }
  public string Describe(string prefix) { return prefix + label + "=" + n; }
  public int Sum(int[] a) { int t = 0; for (int i = 0; i < a.Length; i++) t += a[i] + n; return t; }
  public void Fill(int[] a) { for (int i = 0; i < a.Length; i++) a[i] = n + i; }
}

[Managed]
class Holder {
  public static int Use(Counter c) { return c.Add(1); }
  public static int Both(Counter a, Counter b) { return a.Count * 100 + b.Count; }
  public static string Name(Counter c, string s) { return c.Describe(s); }
}

class Program {
  static int Main() {
    Counter c = new Counter(5);
    c.Add(2);
    c.Count = c.Count + 10;
    Console.WriteLine(c.Count);
    Console.WriteLine(Holder.Use(c));
    c.Label = "n";
    Console.WriteLine(c.Describe("<"));
    Console.WriteLine(c.Positive ? 1 : 0);
    Console.WriteLine((long)(c.Half * 1000.0));
    Console.WriteLine(c.Big(3000000000L));
    int[] xs = new int[3]; xs[0] = 1; xs[1] = 2; xs[2] = 3;
    Console.WriteLine(c.Sum(xs));
    int[] ys = new int[4];
    c.Fill(ys);
    Console.WriteLine(ys[0] + " " + ys[3]);
    Counter d = new Counter(-4);
    Console.WriteLine(d.Positive ? 1 : 0);
    Console.WriteLine(Holder.Both(c, d));
    d.Label = "d";
    Console.WriteLine(Holder.Name(d, "!"));
    return 0;
  }
}
