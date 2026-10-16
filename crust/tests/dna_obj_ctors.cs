// dna
using System;
class ManagedAttribute : Attribute { }

// A managed class with several constructors, and a native class that takes a managed object and calls it.
[Managed]
class Shape {
  int w, h;
  string name;
  public Shape() { w = 1; h = 1; name = "unit"; }
  public Shape(int side) { w = side; h = side; name = "square"; }
  public Shape(int width, int height, string n) { w = width; h = height; name = n; }
  public int Area() { return w * h; }
  public string Name { get { return name; } }
}

class Gauge {
  public static int Measure(Shape s) { return s.Area() * 10; }
}

class Program {
  static int Main() {
    Shape a = new Shape();
    Shape b = new Shape(4);
    Shape c = new Shape(3, 5, "rect");
    Console.WriteLine(a.Area() + " " + a.Name);
    Console.WriteLine(b.Area() + " " + b.Name);
    Console.WriteLine(c.Area() + " " + c.Name);
    Console.WriteLine(Gauge.Measure(a) + Gauge.Measure(b) + Gauge.Measure(c));
    return 0;
  }
}
