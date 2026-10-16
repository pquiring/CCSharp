// refuse: an arena class may derive from one other arena class and nothing else
using System;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }
class Base { public int X; }
[MaxInstances(4)] class Derived : Base { public int Y; }
class Program { static int Main() { Derived d = new Derived(); return d.Y; } }
