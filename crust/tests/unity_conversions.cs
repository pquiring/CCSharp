// unity-types
using System;
using UnityEngine;

// User-defined conversions between the Unity value types: `Vector3 p = new Vector2(..)`, as Unity code writes it all the time.
class Body {
  public Vector3 position;
  public Vector3 Pos() { return position; }
  public Vector2 Flat() { return position; }                // Vector3 -> Vector2, in a return
  public void MoveTo(Vector3 p) { position = p; }
}

class Program {
  static float SumXY(Vector2 v) { return v.x + v.y; }
  static int Show(Vector3 v) { return (int)(v.x * 100f) * 10000 + (int)(v.y * 100f) * 100 + (int)(v.z * 100f); }

  static int Main() {
    Body b = new Body();
    b.position = new Vector2(1f, 2f);                        // assignment, from a constructor call (z becomes 0)
    Console.WriteLine(Show(b.Pos()));
    Vector3 c = new Vector3(3f, 4f, 5f);
    Vector2 d = c;                                           // initialiser, Vector3 -> Vector2 (z dropped)
    Console.WriteLine((int)SumXY(d));
    Console.WriteLine((int)SumXY(c));                        // argument
    b.MoveTo(d);                                             // argument, Vector2 -> Vector3
    Console.WriteLine(Show(b.Pos()));
    Vector3 e = (Vector3)d;                                  // an explicit cast uses the same operator
    Console.WriteLine(Show(e));
    Vector2 f = b.Flat();
    Console.WriteLine((int)(f.x + f.y));
    return 0;
  }
}
