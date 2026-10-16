// dna
using System;
class ManagedAttribute : Attribute { }
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }

// the "engine": arena classes, native.  Their objects have a fixed address, so managed code can hold them by it.
[MaxInstances(8)]
class Node {
  public int Id;
  public float X, Y;
  public bool Alive;
  public Node Parent;
  public const int Limit = 8;
  public static int Created;
  public Node(int id) { Id = id; Alive = true; }
  public static Node Make(int id) { Created++; return new Node(id); }
  public void SetPosition(float x, float y) { X = x; Y = y; }
  public float WorldX() { float w = X; Node p = Parent; while (p != null) { w += p.X; p = p.Parent; } return w; }
  public bool IsDescendantOf(Node other) { Node p = Parent; while (p != null) { if (p == other) return true; p = p.Parent; } return false; }
  public void ToWorld(float lx, float ly, out float wx, out float wy) { wx = WorldX() + lx; wy = Y + ly; }
  public int Depth { get { int d = 0; Node p = Parent; while (p != null) { d++; p = p.Parent; } return d; } }
}

// the game: managed
[Managed]
class Script {
  public static int Run() {
    Node a = new Node(1);                 // managed code makes a native object
    Node b = Node.Make(2);
    b.Parent = a;                         // a field of arena type
    a.SetPosition(10f, 5f);
    b.SetPosition(1f, 2f);
    Console.WriteLine((int)(b.WorldX() * 1000f));
    Console.WriteLine(b.IsDescendantOf(a) ? 1 : 0);
    Console.WriteLine(a.IsDescendantOf(b) ? 1 : 0);
    float wx, wy;
    b.ToWorld(1f, 1f, out wx, out wy);
    Console.WriteLine((int)wx + " " + (int)wy);
    Console.WriteLine(b.Depth);
    Console.WriteLine(b.Parent == a ? 1 : 0);        // the same object is the same reference
    Console.WriteLine(a.Parent == null ? 1 : 0);     // and nothing is null
    Console.WriteLine(Node.Created + " " + Node.Limit);
    Node c = b.Parent;
    c.X = 3f;
    Console.WriteLine((int)a.X);
    a.Alive = false;
    Console.WriteLine(a.Alive ? 1 : 0);
    Console.WriteLine(c == a ? 1 : 0);
    return b.Id * 10 + a.Id;
  }
  // a native object comes in and goes out
  public static int Describe(Node n) { return n.Id * 100 + (n.Parent == null ? 0 : n.Parent.Id); }
  public static Node Pick(Node a, Node b, bool first) { return first ? a : b; }
}

class Program {
  static int Main() {
    Console.WriteLine(Script.Run());
    Node p = new Node(7);
    Node q = new Node(8);
    q.Parent = p;
    Console.WriteLine(Script.Describe(q));
    Console.WriteLine(Script.Pick(p, q, true).Id);
    Console.WriteLine(Script.Pick(p, q, false).Id);
    Console.WriteLine(Script.Pick(p, q, false) == q ? 1 : 0);
    return Node.Created;
  }
}
