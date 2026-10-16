using System;
class Prog {
  static int Hear(Animal a) { return a.Sound() * 10 + a.Legs; }
  public static int Main() {
    Dog d = new Dog();
    d.Legs = 4;
    Puppy p = new Puppy();
    p.Legs = 3;
    Console.WriteLine(Hear(d));      // Dog -> Animal, base declared in another file
    Console.WriteLine(Hear(p));      // Puppy -> Dog -> Animal: two links, in two files
    return 0;
  }
}
