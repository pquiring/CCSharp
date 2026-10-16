using System;
using System.Collections.Generic;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }

// Mutually referencing arena classes, declared in the order that needs a forward reference.
[MaxInstances(8)]
class Owner {
  public int Id;
  public Part First;                      // refers to a class declared below
  public Owner(int id) { Id = id; }
  public Part Add(int w) { Part p = new Part(w); p.Of = this; p.NextOnOwner = First; First = p; return p; }
  public int Total() { int t = 0; for (Part p = First; p != null; p = p.NextOnOwner) t += p.Weight; return t; }
}

[MaxInstances(32)]
class Part {
  public int Weight;
  public Owner Of;
  public Part NextOnOwner;
  public Part(int w) { Weight = w; }
  public Part Self() { return this; }                       // `this` is a reference, and may be stored or returned
  public int OwnerId { get { return Of == null ? -1 : Of.Id; } }
  public Part Link { get; set; }                            // an auto-property of arena type
}

class Registry {
  static Part[] slots;
  static List<Owner> owners;
  public static void Init() {
    slots = new Part[4];                                    // new T[n] of arena type: n nulls
    owners = new List<Owner>();                             // a List of references
  }
  public static void Put(int i, Part p) { slots[i] = p; }
  public static Part Get(int i) { return slots[i]; }
  public static void Register(Owner o) { owners.Add(o); }
  public static int Count() { return owners.Count; }
  public static Owner At(int i) { return owners[i]; }
  public static int Sum() { int t = 0; foreach (Owner o in owners) t += o.Total(); return t; }
  public static int Filled() { int c = 0; for (int i = 0; i < slots.Length; i++) if (slots[i] != null) c++; return c; }
}

class Program {
  static void Swap(ref Part a, ref Part b) { Part t = a; a = b; b = t; }
  static bool TryFind(Owner o, int w, out Part found) {
    for (Part p = o.First; p != null; p = p.NextOnOwner) if (p.Weight == w) { found = p; return true; }
    found = null;
    return false;
  }
  static Part Pick(bool first, Part a, Part b) { return first ? a : b; }
  static Part None(bool give, Part p) { return give ? p : null; }

  static int Main() {
    Registry.Init();
    Owner o1 = new Owner(1);
    Owner o2 = new Owner(2);
    Registry.Register(o1);
    Registry.Register(o2);
    Part a = o1.Add(10);
    Part b = o1.Add(20);
    Part c = o2.Add(5);
    Console.WriteLine(o1.Total() + " " + o2.Total() + " " + Registry.Sum());
    Console.WriteLine(a.OwnerId + " " + b.OwnerId + " " + c.OwnerId);
    Console.WriteLine(a.Self() == a ? 1 : 0);
    Console.WriteLine(a.Self() == b ? 1 : 0);
    Registry.Put(0, a);
    Registry.Put(2, c);
    Console.WriteLine(Registry.Filled());
    Console.WriteLine(Registry.Get(0).Weight + " " + (Registry.Get(1) == null ? 1 : 0) + " " + Registry.Get(2).Weight);
    Swap(ref a, ref b);
    Console.WriteLine(a.Weight + " " + b.Weight);
    Part found;
    Console.WriteLine(TryFind(o1, 10, out found) ? found.Weight : -1);
    Console.WriteLine(TryFind(o1, 99, out found) ? 1 : (found == null ? 0 : 2));
    Console.WriteLine(Pick(true, a, b).Weight + " " + Pick(false, a, b).Weight);
    Console.WriteLine(None(true, a) == a ? 1 : 0);
    Console.WriteLine(None(false, a) == null ? 1 : 0);
    a.Link = b;
    b.Link = c;
    Console.WriteLine(a.Link.Link.Weight);
    Console.WriteLine(c.Link == null ? 1 : 0);
    Owner viaList = Registry.At(1);
    Console.WriteLine(viaList.Id + " " + viaList.First.Weight);
    return Registry.Count();
  }
}
