using System;

struct Handle {
  public int Index;
  public int Gen;
  public Handle(int i, int g) { Index = i; Gen = g; }
}

class Table {
  public int[] gens;
  public Table() { gens = new int[4]; gens[1] = 7; }
  public bool Live(Handle h) { return gens[h.Index] == h.Gen; }
  public int Sum(Handle a, Handle b) { return a.Index + a.Gen + b.Index + b.Gen; }
}

class Program {
  static int Main() {
    Table t = new Table();
    int k = 1;
    Console.WriteLine(t.Live(new Handle(1, 7)) ? 1 : 0);          // a struct built in the call: a constructor call is not an expression in C
    Console.WriteLine(t.Live(new Handle(k, 8)) ? 1 : 0);
    Handle second = new Handle(k + 1, 3);
    Console.WriteLine(t.Sum(new Handle(1, 2), second));          // (a second constructor call in the same call would have to be evaluated first: it is passed from a local)
    return 0;
  }
}
