using System;

// default parameter values: the arguments a call leaves out are filled in from the declaration (numbers, bools, an enum, a string, an arena reference that is
// null, default(struct)), for static and instance methods and for constructors
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }
enum Mode { Fast, Slow, Auto }
struct V { public int A, B; public V(int a, int b) { A = a; B = b; } }

[MaxInstances(8)]
class Node {
  public int Id;
  public Node Next;
  public Node(int id = 3, Node next = null) { Id = id; Next = next; }
}

class Calc {
  public int Base;
  public Calc(int b = 10) { Base = b; }
  public int Add(int a, int b = 5, int c = -1) { return Base + a + b * 10 + c * 100; }
  public static double Scale(double x, double k = 2.5, bool neg = false) { double r = x * k; return neg ? -r : r; }
  public static int Pick(int x, Mode m = Mode.Auto) { return x * 10 + (int)m; }
  public static string Greet(string who = "world", int times = 2) { return who + " x" + times; }
  public static int Walk(Node n = null) { int c = 0; while (n != null) { c = c * 10 + n.Id; n = n.Next; } return c; }
  public static int Sum(V v = default(V), long big = 5000000000L, uint u = 4000000000u) { return v.A + v.B + (int)(big / 1000000000L) + (int)(u / 1000000000u); }
  public static float Half(float f = 0.5f, byte b = 200, short s = -3) { return f * 2 + b + s; }
}

class Program {
  static int Main() {
    Calc c = new Calc();
    Calc d = new Calc(1);
    Console.WriteLine(c.Add(1) + " " + c.Add(1, 2) + " " + c.Add(1, 2, 3) + " " + d.Add(0));
    Console.WriteLine((int)(Calc.Scale(2.0) * 100) + " " + (int)(Calc.Scale(2.0, 4.0) * 100) + " " + (int)(Calc.Scale(2.0, 4.0, true) * 100));
    Console.WriteLine(Calc.Pick(1) + " " + Calc.Pick(1, Mode.Fast) + " " + Calc.Pick(2, Mode.Slow));
    Console.WriteLine(Calc.Greet());
    Console.WriteLine(Calc.Greet("hi"));
    Console.WriteLine(Calc.Greet("yo", 5));
    Node a = new Node();
    Node b = new Node(7, a);
    Node e = new Node(9);
    Console.WriteLine(Calc.Walk() + " " + Calc.Walk(b) + " " + e.Id + " " + (e.Next == null ? 1 : 0) + " " + a.Id);
    Console.WriteLine(Calc.Sum() + " " + Calc.Sum(new V(1, 2)) + " " + Calc.Sum(new V(3, 4), 1000000000L));
    Console.WriteLine((int)(Calc.Half() * 100) + " " + (int)(Calc.Half(1.5f) * 100) + " " + (int)(Calc.Half(1.5f, 10, 4) * 100));
    return c.Add(2) % 100;
  }
}
