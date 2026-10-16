// dna
using System;
class ManagedAttribute : Attribute { }

// A managed object's method calls native code, which makes another managed object and calls that: object -> native -> object -> native ...
[Managed]
class Walker {
  int steps;
  public Walker(int s) { steps = s; }
  public int Steps { get { return steps; } }
  public int Step(int depth) { steps++; if (depth <= 0) return steps; return Gate.Pass(depth - 1, steps) + 1; }
}

class Gate {
  public static int Pass(int depth, int seed) { Walker w = new Walker(seed); return w.Step(depth); }
  public static int Kick(Walker w) { return w.Step(6); }
}

class Program {
  static int Main() {
    Walker w = new Walker(100);
    Console.WriteLine(Gate.Kick(w));
    Console.WriteLine(w.Steps);
    Walker v = new Walker(0);
    Console.WriteLine(v.Step(3));
    Console.WriteLine(Gate.Kick(v) + v.Steps);
    Console.WriteLine(Gate.Pass(8, 1));
    return 0;
  }
}
