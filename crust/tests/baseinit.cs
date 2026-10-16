using System;
class A { public int X; public A(int x) { X = x; } public virtual int F() { return X; } }
class B : A { public int Y; public B(int x, int y) : base(x) { Y = y; } public override int F() { return X + Y; } }
class C : B { public C() : base(1, 2) { } public override int F() { return base_calc() * 2; } int base_calc() { return X + Y + 10; } }
class Prog {
  static int Call(A a) { return a.F(); }
  public static int Main() {
    B b = new B(3, 4);
    C c = new C();
    Console.WriteLine(Call(b));
    Console.WriteLine(Call(c));
    return 0;
  }
}
