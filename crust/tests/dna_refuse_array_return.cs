// dna
// refuse: returning an array from native to managed
using System;
class ManagedAttribute : Attribute { }
class Vec { public static int[] Make(int n) { int[] r = new int[n]; return r; } }
[Managed] class Script { public static int Go() { int[] a = Vec.Make(3); return a.Length; } }
class Program { static int Main() { return Script.Go(); } }
