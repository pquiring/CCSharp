// refuse: uses its receiver twice in C
using System.Collections.Generic;
class Program {
  static List<int> l = new List<int>();
  static List<int> Get() { return l; }
  static int Main() { Get().Add(1); Get().RemoveAt(0); return 0; }
}
