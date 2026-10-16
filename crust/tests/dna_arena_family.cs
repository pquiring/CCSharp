// dna
using System;
using System.Collections.Generic;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }
[MaxInstances(4)] abstract class Component { public int Id; public virtual int Score() { return Id; } }
class Plain : Component { public Plain(int i) { Id = i; } public override int Score() { return Id * 2; } }
class Scripted : Component {
  public Scripted(int i) { Id = i; }
  public override int Score() { Func<int, int> f = x => x + Id; return f(1); }   // a lambda: cannot be lowered
}
class Program {
  static int Main() {
    Component a = new Plain(3);
    Component b = new Scripted(4);
    Console.WriteLine(a.Score() + " " + b.Score());
    return 0;
  }
}
