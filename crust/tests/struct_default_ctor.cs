using System;
using System.Collections.Generic;

// a struct that declares only constructors with parameters still has a parameterless one in C#: default(V), new V(), as a List element, a field
struct V { public int A, B; public V(int a, int b) { A = a; B = b + 1; } }
struct W { public V Inner; public int N; }
class Program {
  static int Main() {
    V v = default(V);
    V w = new V();
    V x = new V(3, 4);
    List<V> l = new List<V>();
    l.Add(new V());
    l.Add(x);
    W holder = new W();
    holder.N = 5;
    Console.WriteLine(v.A + " " + v.B + " " + w.A + " " + w.B + " " + x.A + " " + x.B);
    Console.WriteLine(l.Count + " " + l[0].A + " " + l[0].B + " " + l[1].B + " " + holder.Inner.A + " " + holder.Inner.B + " " + holder.N);
    return x.B + holder.N;
  }
}
