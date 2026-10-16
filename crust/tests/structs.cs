using System;
struct Vec { public int X, Y;
  public Vec(int x, int y) { X = x; Y = y; }
  public int Dot(Vec o) { return X * o.X + Y * o.Y; }
}
class Prog {
  static Vec Add(Vec a, Vec b) { return new Vec(a.X + b.X, a.Y + b.Y); }
  public static int Main() {
    Vec a = new Vec(1, 2);
    Vec b = a;          // a copy: structs are values
    b.X = 10;
    Console.WriteLine(a.X);
    Console.WriteLine(b.X);
    Vec c = Add(a, b);
    Console.WriteLine(c.X + c.Y);
    Console.WriteLine(a.Dot(b));
    return 0;
  }
}
