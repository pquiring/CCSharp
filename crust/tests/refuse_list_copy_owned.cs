// refuse: copies the elements here
using System.Collections.Generic;
class Owned { public int V; }
class Program {
  static int Main() {
    List<Owned> a = new List<Owned>();
    a.Add(new Owned());
    List<Owned> b = new List<Owned>(a);
    return b.Count;
  }
}
