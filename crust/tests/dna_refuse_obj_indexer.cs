// dna
// refuse: an indexer cannot cross the boundary
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Managed] class Table { int[] a = new int[4]; public int this[int i] { get { return a[i]; } set { a[i] = value; } } }
[Native] class Program { static int Main() { Table t = new Table(); return t[1]; } }
