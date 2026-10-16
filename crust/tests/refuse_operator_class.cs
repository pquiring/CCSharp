// refuse: operator overloading is for a struct
class Money { public int Cents; public static Money operator +(Money a, Money b) { Money m = new Money(); m.Cents = a.Cents + b.Cents; return m; } }
class Program { static int Main() { Money a = new Money(); Money b = new Money(); return (a + b).Cents; } }
