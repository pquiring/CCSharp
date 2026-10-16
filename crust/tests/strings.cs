using System;
using System.Collections.Generic;
class Person {
  public string Name;
  public int Age;
  public string Title { get; set; }
  public string Label { get { return Name + " (" + Age + ")"; } }
  public string Shout => Name + "!";
  public Person(string name, int age) { Name = name; Age = age; }
  public string Describe() { return "I am " + Name + ", aged " + Age; }
  public bool Is(string other) { return Name == other; }
}
class Prog {
  static string Greet(string who) { return "Hello, " + who + "!"; }
  static string Echo(string s) { return s; }
  static int Len(string s) { return s.Length; }
  static string Repeat(string s, int n) {
    string r = "";
    for (int i = 0; i < n; i++) r += s;
    return r;
  }
  public static int Main() {
    string a = "hi";
    string b = a;                       // a copy; strings are immutable so it can't be told apart
    b += " there";
    Console.WriteLine(a);
    Console.WriteLine(b);
    Console.WriteLine(Greet(a));
    Console.WriteLine(Echo(Greet("bob")));
    Console.WriteLine(Len(b));
    Console.WriteLine(Repeat("ab", 3));
    string n = "n=" + 42 + ", neg=" + (-7) + ", big=" + 3000000000L + ", u=" + 4000000000u + ", t=" + true;
    Console.WriteLine(n);
    Console.WriteLine($"interp {a} {Len(b)} {b.Length > 3}");
    Console.WriteLine(long.MinValue + "|" + ulong.MaxValue);
    if (a == "hi") Console.WriteLine("eq-literal");
    if (a != b) Console.WriteLine("ne");
    if (Greet("x") == "Hello, x!") Console.WriteLine("eq-call");
    Person p = new Person("Ann", 30);
    p.Title = "Dr";
    Console.WriteLine(p.Label);
    Console.WriteLine(p.Shout);
    Console.WriteLine(p.Describe());
    Console.WriteLine(p.Title + " " + p.Name);
    Console.WriteLine(p.Is("Ann"));
    Console.WriteLine(p.Is("Bob"));
    List<string> names = new List<string>();
    names.Add("x"); names.Add(Greet("y")); names.Add(a + b);
    int total = 0;
    foreach (string s in names) { Console.WriteLine(s); total += s.Length; }
    Console.WriteLine(names.Count);
    Console.WriteLine(names[1]);
    string e = string.Empty;
    Console.WriteLine(e.Length);
    Console.WriteLine("tab\there \"quoted\" 100% \\ back");
    return total;
  }
}
