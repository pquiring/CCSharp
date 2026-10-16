// refuse: has a side effect that would be lost
using System.Collections.Generic;
class Program {
  static int n;
  static int Next() { n++; return n; }
  static int Main() {
    List<int> a = new List<int>(Next());
    return a.Count + n;
  }
}
