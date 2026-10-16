using System;
using System.Collections.Generic;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }

// A generic method whose type parameter is constrained to an arena class, and `is` / `as` / a cast between arena classes: the objects know their type
// (the engine's GetComponent<T>).
[MaxInstances(8)]
abstract class Part { public int Id; public virtual int Kind() { return 0; } }
class Wheel : Part { public override int Kind() { return 1; } }
class Engine : Part { public int Power; public override int Kind() { return 2; } }
class Turbo : Engine { public override int Kind() { return 3; } }

[MaxInstances(4)]
class Car {
  public List<Part> parts = new List<Part>();
  public T Get<T>() where T : Part {
    foreach (Part p in parts) { T t = p as T; if (t != null) return t; }
    return null;
  }
  public int Count<T>() where T : Part {
    int n = 0;
    foreach (Part p in parts) { if (p is T) n++; }
    return n;
  }
  public T Add<T>(T p) where T : Part { parts.Add(p); return p; }
}

class Program {
  static int Main() {
    Car c = new Car();
    Wheel w = new Wheel(); w.Id = 1;
    Engine e = new Engine(); e.Id = 2; e.Power = 90;
    c.Add(w); c.Add(e); c.Add(new Wheel());
    Console.WriteLine(c.Get<Wheel>().Id + " " + c.Get<Engine>().Power);
    Console.WriteLine(c.Count<Wheel>() + " " + c.Count<Engine>() + " " + c.Count<Part>());
    Console.WriteLine(c.Get<Turbo>() == null ? 1 : 0);
    c.Add(new Turbo());
    Console.WriteLine(c.Count<Engine>() + " " + c.Get<Turbo>().Kind());     // a Turbo is an Engine
    Part q = c.parts[1];
    Engine qe = (Engine)q;
    Console.WriteLine(qe.Power);
    Console.WriteLine(q is Wheel ? 1 : 0);
    return 0;
  }
}
