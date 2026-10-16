// refuse: is for a class
using System;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }
[MaxInstances(4)] struct P { public int X; }
class Program { static int Main() { P p = new P(); return p.X; } }
