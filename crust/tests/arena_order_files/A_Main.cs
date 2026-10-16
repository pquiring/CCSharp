using System;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }

// the arena class and Main are in one file, the pool that holds the class in another that Main also uses: the files use each other, and the
// pool has to come after the class it holds
[MaxInstances(16)]
class Early {
  public int Id;
  public int On;
  public void Reset() { Id = 0; On = 0; }
}

class Program {
  static int Main() {
    Scripts.Init();
    Early a = Scripts.AddEarly();
    a.Id = 3;
    Early b = Scripts.AddEarly();
    b.Id = 4;
    Console.WriteLine(Scripts.Count() + " " + (a.Id + b.Id));
    Scripts.Free(a);
    Early c = Scripts.AddEarly();
    Console.WriteLine(c.Id + " " + Scripts.Count());
    return Scripts.Count();
  }
}
