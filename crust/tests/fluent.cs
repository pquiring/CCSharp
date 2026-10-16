using System;
class Acc {
  int v;
  public Acc Add(int x) { v += x; return this; }
  public Acc Mul(int x) { v *= x; return this; }
  public int V { get { return v; } }
}
class Prog {
  public static int Main() {
    Acc a = new Acc();
    a.Add(1).Add(2).Add(3);
    Console.WriteLine(a.V);        // 6, not 1: the chain mutates the SAME object
    a.Add(4).Mul(2).Add(1);
    Console.WriteLine(a.V);
    return a.V;
  }
}
