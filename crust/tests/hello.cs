using System;

class Counter {
  int n;
  public void Inc() { n += 1; }
  public int Get() { return n; }
}

class Example {
  public static int Main() {
    Counter c = new Counter();
    c.Inc(); c.Inc();
    Console.WriteLine("Hello, Crust!");
    Console.WriteLine($"count = {c.Get()}");
    return c.Get();
  }
}
