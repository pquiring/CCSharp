// dna
using System;
class ManagedAttribute : Attribute { }

// The managed object lives as long as the native variable that holds it: a scope's end releases it.  (The harness runs this with
// CCS_CHECK_HANDLES=1, so a handle that was never released fails the case.)
[Managed]
class Box {
  int v;
  public Box(int x) { v = x; }
  public int Get() { return v; }
  public void Set(int x) { v = x; }
}

class Native {
  public static int Twice(Box b) { return b.Get() * 2; }
  public static void Bump(Box b) { b.Set(b.Get() + 1); }
}

class Program {
  static int Main() {
    int total = 0;
    // 30000 objects, one after the other: if they were not released the table would grow without end
    for (int i = 0; i < 30000; i++) {
      Box b = new Box(i);
      Native.Bump(b);
      total += Native.Twice(b) & 15;
    }
    Console.WriteLine(total);
    // nested scopes, and several alive at once
    Box a = new Box(1);
    {
      Box c = new Box(10);
      {
        Box d = new Box(100);
        Native.Bump(d);
        Console.WriteLine(Native.Twice(d));
      }
      Native.Bump(c);
      Console.WriteLine(Native.Twice(c));
    }
    for (int k = 0; k < 5; k++) {
      Box e = new Box(k);
      Box f = new Box(k * 10);
      total += Native.Twice(e) + Native.Twice(f);
    }
    Native.Bump(a);
    Console.WriteLine(Native.Twice(a));
    Console.WriteLine(total);
    return 0;
  }
}
