// refuse: an arena class may derive from one other arena class and nothing else
using System;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }
interface IShape { int Area(); }
[MaxInstances(4)] class Base { public int X; }
[MaxInstances(4)] class Derived : Base, IShape { public int Y; public int Area() { return Y; } }
class Program { static int Main() { Derived d = new Derived(); return d.Area(); } }
