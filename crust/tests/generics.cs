using System;
class Box<T> {
  public T Value;
  public Box(T v) { Value = v; }
  public T Get() { return Value; }
  public void Set(T v) { Value = v; }
}
class Pair<A, B> {
  public A First; public B Second;
  public Pair(A a, B b) { First = a; Second = b; }
}
class Prog {
  public static int Main() {
    Box<int> b = new Box<int>(41);
    b.Set(b.Get() + 1);
    Console.WriteLine(b.Get());
    Pair<int, long> p = new Pair<int, long>(3, 4000000000L);
    Console.WriteLine(p.First);
    Console.WriteLine(p.Second);
    return 0;
  }
}
