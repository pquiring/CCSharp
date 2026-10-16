// unity-types
using System;
using UnityEngine;

// --unity-types: Vector2, Vector3, Quaternion and Color are built in, with Unity's operators. Floats are printed as ints (a float cannot be printed).
class Program {
  static int I(float f) { return (int)Math.Floor(f * 100f + 0.5f); }
  static int Main() {
    Vector2 a = new Vector2(1f, 2f);
    Vector2 b = new Vector2(3f, 4f);
    Vector2 s = a + b;
    Vector2 d = b - a;
    Vector2 n = -a;
    Vector2 k = a * 2f;
    Vector2 k2 = 3f * a;
    Vector2 h = b / 2f;
    Console.WriteLine(I(s.x) + " " + I(s.y) + " " + I(d.x) + " " + I(d.y) + " " + I(n.x) + " " + I(n.y));
    Console.WriteLine(I(k.x) + " " + I(k.y) + " " + I(k2.x) + " " + I(k2.y) + " " + I(h.x) + " " + I(h.y));
    Console.WriteLine((a == a ? 1 : 0) + " " + (a == b ? 1 : 0) + " " + (a != b ? 1 : 0));
    Vector2 acc = Vector2.zero;
    for (int i = 0; i < 3; i++) { acc += b; acc -= a; }
    Console.WriteLine(I(acc.x) + " " + I(acc.y) + " " + I(acc.magnitude) + " " + I(Vector2.Distance(a, b)));
    Vector3 u = new Vector3(1f, 0f, 0f);
    Vector3 w = new Vector3(0f, 1f, 0f);
    Vector3 c = Vector3.Cross(u, w);
    Vector3 sum = u + w * 2f;
    Console.WriteLine(I(c.z) + " " + I(sum.x) + " " + I(sum.y) + " " + I(Vector3.Dot(sum, w)));
    Quaternion q = Quaternion.Euler(0f, 0f, 90f);
    Vector3 r = q * u;
    Quaternion q2 = q * q;
    Vector3 eul = q.eulerAngles;
    Console.WriteLine(I(r.x) + " " + I(r.y) + " " + I(q2.z) + " " + I(eul.z));
    Color red = Color.red;
    Color blue = Color.blue;
    Color mix = Color.Lerp(red, blue, 0.5f);
    Color sc = red * 0.5f;
    Color both = red + blue;
    Console.WriteLine(I(mix.r) + " " + I(mix.b) + " " + I(sc.r) + " " + I(both.b));
    return 0;
  }
}
