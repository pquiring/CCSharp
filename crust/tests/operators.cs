using System;
using System.Collections.Generic;

// operator overloading on a struct: a use is a call of the operator's static method. Binary and unary operators, ones that differ by operand type, comparison,
// compound assignment, nesting, and as arguments, conditions and list elements
struct Vec {
  public int X, Y;
  public Vec(int x, int y) { X = x; Y = y; }
  public static Vec operator +(Vec a, Vec b) { return new Vec(a.X + b.X, a.Y + b.Y); }
  public static Vec operator -(Vec a, Vec b) { return new Vec(a.X - b.X, a.Y - b.Y); }
  public static Vec operator -(Vec a) { return new Vec(-a.X, -a.Y); }
  public static Vec operator *(Vec a, int k) { return new Vec(a.X * k, a.Y * k); }
  public static Vec operator *(int k, Vec a) { return new Vec(a.X * k, a.Y * k); }
  public static int operator *(Vec a, Vec b) { return a.X * b.X + a.Y * b.Y; }
  public static bool operator ==(Vec a, Vec b) { return a.X == b.X && a.Y == b.Y; }
  public static bool operator !=(Vec a, Vec b) { return !(a == b); }
  public static bool operator <(Vec a, Vec b) { return a.X * a.X + a.Y * a.Y < b.X * b.X + b.Y * b.Y; }
  public static bool operator >(Vec a, Vec b) { return b < a; }
#if !CRUST
  public override bool Equals(object o) { return o is Vec && this == (Vec)o; }
  public override int GetHashCode() { return X * 31 + Y; }
#endif
}

class Program {
  static int Len2(Vec v) { return v * v; }
  static int Main() {
    Vec a = new Vec(1, 2), b = new Vec(10, 20), c = new Vec(-3, 4);
    Vec s = a + b;
    Vec d = b - a;
    Vec n = -c;
    Vec m = a * 3;
    Vec m2 = 2 * b;
    Console.WriteLine(s.X + " " + s.Y + " " + d.X + " " + d.Y + " " + n.X + " " + n.Y + " " + m.X + " " + m.Y + " " + m2.X + " " + m2.Y);
    Console.WriteLine((a * b) + " " + Len2(c) + " " + Len2(a + b * 2 - c));
    Vec e = a + b * 2 - c;
    Console.WriteLine(e.X + " " + e.Y);
    Console.WriteLine((a == a ? 1 : 0) + " " + (a == b ? 1 : 0) + " " + (a != b ? 1 : 0) + " " + (a < b ? 1 : 0) + " " + (a > b ? 1 : 0) + " " + (c > a ? 1 : 0));
    Vec acc = new Vec(0, 0);
    for (int i = 1; i <= 4; i++) { acc += new Vec(i, i * i); acc -= new Vec(1, 0); }
    Console.WriteLine(acc.X + " " + acc.Y);
    if (a + b == new Vec(11, 22)) Console.WriteLine("sum ok");
    List<Vec> l = new List<Vec>();
    l.Add(a + a); l.Add(-b);
    Console.WriteLine(l.Count + " " + l[0].X + " " + l[1].Y + " " + ((l[0] + l[1]).X));
    return Len2(e) % 200;
  }
}
