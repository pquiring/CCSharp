// refuse: `in` parameters are not in the Crust C# subset
using System;
// Without System.Runtime.InteropServices.InAttribute in the corelib an `in` parameter does not even bind (CS0518). With it the program
// reaches the emitter, which refuses the parameter by name, like every other construct outside the subset.
struct Vec { public int X; public int Y; }
class Prog {
  static int Sum(in Vec v) { return v.X + v.Y; }
  public static int Main() {
    Vec v = new Vec();
    v.X = 3; v.Y = 4;
    Console.WriteLine(Sum(in v));
    Console.WriteLine(Sum(v));
    return 0;
  }
}
