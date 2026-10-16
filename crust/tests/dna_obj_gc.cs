// dna
using System;
class ManagedAttribute : Attribute { }

// Objects held by native code only, each with objects of its own (an array, a string), while the managed code allocates enough
// that the collector runs again and again.  What a held object refers to must stay alive as long as the object does.
[Managed]
class Bag {
  int[] items;
  string name;
  int count;
  public Bag(string n, int size) { name = n; items = new int[size]; }
  public void Put(int v) { items[count % items.Length] = v; count++; }
  public int Total() { int t = 0; for (int i = 0; i < items.Length; i++) t += items[i]; return t; }
  public string Name { get { return name; } }
  public int Count { get { return count; } }
}

[Managed]
class Churn {
  public static int Garbage(int rounds) {
    int sink = 0;
    for (int i = 0; i < rounds; i++) {
      int[] g = new int[64];
      g[0] = i;
      string s = "g" + i;
      sink += g[0] + s.Length;
    }
    return sink & 1;
  }
}

class Program {
  static int Main() {
    Bag a = new Bag("alpha", 8);
    Bag b = new Bag("beta", 16);
    Bag c = new Bag("gamma", 4);
    int check = 0;
    for (int round = 0; round < 40; round++) {
      a.Put(round); b.Put(round * 2); c.Put(round * 3);
      check += Churn.Garbage(2000);
    }
    Console.WriteLine(a.Total() + " " + b.Total() + " " + c.Total());
    Console.WriteLine(a.Name + " " + b.Name + " " + c.Name);
    Console.WriteLine(a.Count + b.Count + c.Count);
    Console.WriteLine(check >= 0 ? 1 : 0);
    return 0;
  }
}
