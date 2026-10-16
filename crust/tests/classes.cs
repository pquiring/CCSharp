using System;
interface IShape { int Area(); int Sides(); }
abstract class Base : IShape {
  public int Id { get; set; }
  public abstract int Area();
  public virtual int Sides() { return 0; }
  public int Twice() { return Area() * 2; }
}
class Sq : Base {
  public int S;
  public static int Count = 0;
  public Sq(int s) { S = s; Count++; }
  public override int Area() { return S * S; }
  public override int Sides() { return 4; }
}
class Tri : Base {
  public int B, H;
  public Tri(int b, int h) { B = b; H = h; }
  public override int Area() { return B * H / 2; }
  public override int Sides() { return 3; }
}
class Prog {
  static int Describe(IShape s) { return s.Area() * 100 + s.Sides(); }
  static int Total(Base b) { return b.Twice() + b.Id; }
  public static int Main() {
    Sq q = new Sq(3);
    q.Id = 7;
    Tri t = new Tri(6, 5);
    t.Id = 1;
    Console.WriteLine(Describe(q));
    Console.WriteLine(Describe(t));
    Console.WriteLine(Total(q));
    Console.WriteLine(Total(t));
    Console.WriteLine(Sq.Count);
    q.S += 2;
    Console.WriteLine(q.Area());
    return 0;
  }
}
