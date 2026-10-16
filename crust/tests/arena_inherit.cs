using System;
using System.Collections.Generic;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }

// Arena classes may derive from an arena class: a reference to the derived class is the address of the object, so it is also a reference to its base.
// A class that derives from an arena class is an arena class itself, with the base's N unless it says its own.
[MaxInstances(4)]
abstract class Component {                          // abstract: never constructed, so its N only sets the default for the classes below
  public int Id;
  public Entity Owner;                              // refers to a class declared below
  public Component Next;                            // a base-typed reference to the same family
  public Component(int id) { Id = id; }
  public virtual void Update() { }
  public virtual int Score() { return Id; }
  public int OwnerTag { get { return Owner == null ? -1 : Owner.Tag; } }
}

class Mover : Component {                           // inherits N = 4
  public int Speed;
  public Mover(int id, int speed) : base(id) { Speed = speed; }
  public override void Update() { Id += Speed; }
  public override int Score() { return Id * 2; }
}

class Turbo : Mover {                               // a third level: Turbo -> Mover -> Component
  public Turbo(int id) : base(id, 100) { }
  public override void Update() { Id += Speed + 1; }
  public override int Score() { return Id * 3; }
}

[MaxInstances(2)]
class Spinner : Component {                         // its own N
  public int Turns;
  public Spinner(int id) : base(id) { Turns = 0; }
  public override void Update() { Turns++; Id += Turns; }
}

[MaxInstances(8)]
class Entity {
  public int Tag;
  public Component First;
  public Entity(int tag) { Tag = tag; }
  public Component Add(Component c) { c.Owner = this; c.Next = First; First = c; return c; }
  public int Total() { int t = 0; for (Component c = First; c != null; c = c.Next) t += c.Score(); return t; }
  public void Tick() { for (Component c = First; c != null; c = c.Next) c.Update(); }
}

class Program {
  static List<Component> all;
  static int Run(Component c) { c.Update(); return c.Score(); }          // a derived reference passed as its base
  static Component Pick(bool first, Component a, Component b) { return first ? a : b; }
  static Component None(bool give, Component c) { return give ? c : null; }

  static int Main() {
    all = new List<Component>();
    Entity e = new Entity(7);
    Mover m = new Mover(1, 10);
    Spinner s = new Spinner(100);
    e.Add(m);                                       // upcast in an argument
    e.Add(s);
    Component b = m;                                // upcast in an initialiser
    Console.WriteLine(b == m ? 1 : 0);
    Console.WriteLine(b.Score() + " " + s.Score());
    Console.WriteLine(Run(m) + " " + Run(s));       // virtual dispatch through the base
    e.Tick();
    Console.WriteLine(m.Id + " " + s.Id + " " + s.Turns);
    Console.WriteLine(e.Total());
    Console.WriteLine(m.OwnerTag + " " + s.OwnerTag);
    all.Add(m);
    all.Add(s);
    all.Add(new Mover(5, 1));                       // a fresh temporary into a List of the base
    int t = 0;
    foreach (Component c in all) { c.Update(); t += c.Score(); }
    Console.WriteLine(t);
    Console.WriteLine(Pick(true, m, s).Score() + " " + Pick(false, m, s).Score());
    Console.WriteLine(None(false, m) == null ? 1 : 0);
    Console.WriteLine(None(true, s) == s ? 1 : 0);
    Component tb = new Turbo(2);                    // a grandchild, held as the base
    all.Add(tb);
    Console.WriteLine(Run(tb) + " " + tb.Score());
    Mover tm = new Turbo(3);                        // and as its direct base
    Console.WriteLine(Run(tm) + " " + tm.Speed);
    Component d = null;
    Console.WriteLine(d == null ? 1 : 0);
    d = s;                                          // assignment of a derived reference to a base variable
    Console.WriteLine(d.Score());
    return 0;
  }
}
