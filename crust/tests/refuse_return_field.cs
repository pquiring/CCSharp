// refuse: returning an existing object
class Part { public int V; }
class Own { Part p = new Part(); public Part Get() { return p; } }
class P { public static int Main() { return 0; } }
