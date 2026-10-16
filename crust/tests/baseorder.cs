using System;
namespace Game.Core {
  class Derived : Baseish { public int D; public int Sum() { return B + D; } }
  class Leaf : Derived { public int L = 100; public int All() { return Sum() + L; } }
  class Baseish { public int B = 5; }
  class Owner {
    public Leaf Part = new Leaf();
    public int[] Data;
    public Owner() { Data = new int[3]; Data[2] = 9; Part.D = 4; }
    public int Total() { return Part.All() + Data[2]; }
  }
  class Prog {
    public static int Main() {
      Owner o = new Owner();
      Console.WriteLine(o.Total());
      return 0;
    }
  }
}
