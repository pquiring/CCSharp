using System;
using System.Collections.Generic;

// List<T>.Capacity = n is a hint (a real reserve): nothing a program can see changes. float / double classification.
struct Pt { public int X; }
class Program {
  static int Main() {
    List<int> a = new List<int>();
    a.Capacity = 64;
    Console.WriteLine(a.Count);
    for (int i = 0; i < 100; i++) a.Add(i);
    a.Capacity = 200;
    Console.WriteLine(a.Count + " " + a[99]);
    List<Pt> p = new List<Pt>();
    p.Capacity = a.Count / 4;
    Pt q = new Pt(); q.X = 3;
    p.Add(q);
    Console.WriteLine(p.Count + " " + p[0].X);
    float nan = 0f / 0f; float one = 1f; float inf = 1f / 0f;
    double dnan = 0.0 / 0.0; double done = 2.0; double dinf = -1.0 / 0.0;
    Console.WriteLine((float.IsNaN(nan) ? 1 : 0) + " " + (float.IsNaN(one) ? 1 : 0) + " " + (float.IsInfinity(inf) ? 1 : 0) + " " + (float.IsInfinity(one) ? 1 : 0));
    Console.WriteLine((double.IsNaN(dnan) ? 1 : 0) + " " + (double.IsNaN(done) ? 1 : 0) + " " + (double.IsInfinity(dinf) ? 1 : 0) + " " + (double.IsNegativeInfinity(dinf) ? 1 : 0) + " " + (double.IsPositiveInfinity(dinf) ? 1 : 0));
    return a.Count + p.Count;
  }
}
