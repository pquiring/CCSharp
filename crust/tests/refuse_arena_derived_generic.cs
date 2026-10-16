// refuse: an arena class cannot be generic
using System;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }
[MaxInstances(4)] class Base { public int X; }
class Box<T> : Base { public int Y; }
class Program { static int Main() { Box<int> b = new Box<int>(); return b.Y; } }
