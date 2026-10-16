// refuse: returns `this` and something else
class Acc { public Acc Add(int x) { if (x > 0) return this; return new Acc(); } }
class P { public static int Main() { return 0; } }
