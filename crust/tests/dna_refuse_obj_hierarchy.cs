// dna
// refuse: a class hierarchy cannot cross the boundary
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Managed] class Animal { public int Legs() { return 4; } }
[Managed] class Dog : Animal { public int Bark() { return 1; } }
[Native] class Program { static int Main() { Dog d = new Dog(); return d.Bark(); } }
