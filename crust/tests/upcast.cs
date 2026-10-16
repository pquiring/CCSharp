using System;
interface IShape { int Area(); }
class Shape : IShape { public int Id; public virtual int Area() { return 0; } }
class Sq : Shape { public int S; public Sq(int s) { S = s; } public override int Area() { return S * S; } }
class Cube : Sq { public Cube(int s) : base(s) { } public override int Area() { return 6 * S * S; } }
class Prog {
  static int ViaBase(Shape s) { return s.Area() + 1; }
  static int ViaIface(IShape s) { return s.Area() + 2; }
  static int Two(Shape a, Shape b) { return a.Area() * 100 + b.Area(); }
  public static int Main() {
    Sq q = new Sq(3);
    Cube c = new Cube(2);
    Console.WriteLine(ViaBase(q));          // virtual dispatch must still reach Sq.Area
    Console.WriteLine(ViaBase(c));
    Console.WriteLine(ViaIface(c));
    Console.WriteLine(Two(q, c));
    Console.WriteLine(ViaBase(new Sq(4)));  // a fresh temporary, then upcast
    Shape plain = new Shape();
    Console.WriteLine(ViaBase(plain));
    return 0;
  }
}
