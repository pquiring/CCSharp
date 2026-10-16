// refuse: an arena class cannot be generic
using System;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }
[MaxInstances(4)] class Box<T> { public T V; }
class Program { static int Main() { Box<int> b = new Box<int>(); return b.V; } }
