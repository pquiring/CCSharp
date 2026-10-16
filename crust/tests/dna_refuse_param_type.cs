// dna
// refuse: cannot cross
using System;
using System.Collections.Generic;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Managed] class Script { public static int Go(List<int> l) { return l.Count; } }
[Native] class Program { static int Main() { List<int> l = new List<int>(); l.Add(1); return Script.Go(l); } }
