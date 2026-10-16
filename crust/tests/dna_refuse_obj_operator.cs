// dna
// refuse: That kind of member cannot cross the boundary
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Managed] class Money {
  int c;
  public Money(int x) { c = x; }
  public static int operator -(Money a, Money b) { return a.c - b.c; }
}
[Native] class Program { static int Main() { Money a = new Money(1); Money b = new Money(2); int d = a - b; return d; } }
