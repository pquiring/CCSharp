using System;
using System.Collections.Generic;

// new List<T>(capacity) is an EMPTY list (a C++ vector(n) would have n elements); new List<T>(list) is a copy; Reverse reverses in place
struct Pt { public int X, Y; public Pt(int x, int y) { X = x; Y = y; } }

class Program {
  static int Main() {
    List<int> a = new List<int>(100);
    Console.WriteLine(a.Count);
    for (int i = 0; i < 6; i++) a.Add(i * i);
    List<int> b = new List<int>(a);
    b.Add(99);
    a[0] = 7;
    Console.WriteLine(a.Count + " " + b.Count + " " + a[0] + " " + b[0] + " " + b[6]);
    b.Reverse();
    Console.WriteLine(b[0] + " " + b[1] + " " + b[6] + " " + a[5]);
    List<Pt> pts = new List<Pt>(a.Count + 1);
    for (int i = 0; i < 4; i++) pts.Add(new Pt(i, -i));
    List<Pt> copy = new List<Pt>(pts);
    copy.Reverse();
    pts[0] = new Pt(50, 50);
    Console.WriteLine(pts.Count + " " + pts[0].X + " " + copy[0].X + " " + copy[3].X + " " + copy[1].Y);
    List<int> odd = new List<int>();
    odd.Add(1); odd.Add(2); odd.Add(3);
    odd.Reverse();
    Console.WriteLine(odd[0] + " " + odd[1] + " " + odd[2]);
    List<int> none = new List<int>();
    none.Reverse();
    return a.Count + b.Count + odd[0] + none.Count;
  }
}
