#!/usr/bin/env python3
"""test_partition -- `--dna`: which classes are native (Crust C++) and which managed (C#, run by DotNetAnywhere).

    python3 crust/test_partition.py        (python3 build.py test runs it after test_inputs)

Each case is a C# program run through the compiler with --dna; what is checked is the partition it reports
(<name>.partition.json), the diagnostics, and the C# it leaves for the managed side (cpp/managed).
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ccs2c                                             # noqa: E402


class Case(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.mkdtemp(prefix="ccs-part-")

    def tearDown(self):
        shutil.rmtree(self.d, ignore_errors=True)

    def build(self, src, dna=True, extra=()):
        """Compile `src` (one file, Prog.cs); returns (exit status, output)."""
        path = os.path.join(self.d, "Prog.cs")
        with open(path, "w") as f:
            f.write(src)
        listing = os.path.join(self.d, "sources.txt")
        with open(listing, "w") as f:
            f.write(path + "\n")
        cmd = ccs2c.compiler_cmd() + [path, "T", "--crust", "--home=" + ccs2c.REPO, "--srclist=" + listing]
        if dna:
            cmd.append("--dna")
        cmd += list(extra)
        p = subprocess.run(cmd, cwd=self.d, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        return p.returncode, p.stdout.decode("utf-8", "replace")

    def plan(self):
        with open(os.path.join(self.d, "cpp", "T.partition.json")) as f:
            return json.load(f)

    def sides(self):
        return {c["name"]: c["partition"] for c in self.plan()["classes"]}

    def managed_text(self):
        parts = []
        mdir = os.path.join(self.d, "cpp", "managed")
        for n in sorted(os.listdir(mdir)):
            with open(os.path.join(mdir, n)) as f:
                parts.append(f.read())
        return "\n".join(parts)

    def ok(self, src, **kw):
        rc, out = self.build(src, **kw)
        self.assertEqual(rc, 0, out)
        return out


def ccs_corlib():
    return ""


LAMBDA = "System.Func<int> g = () => 1; return g();"


class Default(Case):
    def test_plain_classes_stay_native(self):
        self.ok("class Vec { public int x; public int Add(int a) { return x + a; } }\n"
                "class Program { static int Main() { return 0; } }\n")
        self.assertEqual(self.sides(), {"Vec": "native", "Program": "native"})
        self.assertEqual(self.plan()["entry"]["side"], "native")

    def test_a_refused_class_falls_back_to_managed_with_the_reason(self):
        out = self.ok("class Vec { public int x; }\n"
                      "class Scripted { public int Run() { " + LAMBDA + " } }\n"
                      "class Program { static int Main() { return 0; } }\n")
        self.assertEqual(self.sides(), {"Vec": "native", "Scripted": "managed", "Program": "native"})
        reason = [c for c in self.plan()["classes"] if c["name"] == "Scripted"][0]["reason"]
        self.assertIn("outside the Crust subset", reason)
        self.assertIn("Prog.cs:2", reason)                                   # the line the class was refused at
        self.assertIn("managed Scripted", out)

    def test_without_dna_the_same_class_is_still_refused(self):
        rc, out = self.build("class Scripted { public int Run() { " + LAMBDA + " } }\n"
                             "class Program { static int Main() { return 0; } }\n", dna=False)
        self.assertEqual(rc, 1)
        self.assertIn("construct(s) outside the Crust C# subset", out)
        self.assertFalse(os.path.exists(os.path.join(self.d, "cpp", "T.partition.json")))

    def test_the_native_output_has_only_the_native_classes(self):
        self.ok("class Vec { public int x; }\n"
                "class Scripted { public int Run() { " + LAMBDA + " } }\n"
                "class Program { static int Main() { return 0; } }\n")
        with open(os.path.join(self.d, "cpp", "Prog.cpp")) as f:
            cpp = f.read()
        self.assertIn("class Vec", cpp)
        self.assertIn("class Program", cpp)
        self.assertNotIn("Scripted", cpp)


class Explicit(Case):
    def test_managed_attribute_overrides_a_class_that_could_be_native(self):
        self.ok("using Crust;\n[Managed] class Logger { public int n; }\n"
                "class Program { static int Main() { return 0; } }\n")
        self.assertEqual(self.sides()["Logger"], "managed")
        c = [c for c in self.plan()["classes"] if c["name"] == "Logger"][0]
        self.assertEqual(c["explicit"], "managed")
        self.assertEqual(c["reason"], "marked [Managed]")

    def test_native_attribute_on_a_refused_class_is_an_error_not_a_fallback(self):
        rc, out = self.build("using Crust;\n[Native] class K { public int Run() { " + LAMBDA + " } }\n"
                             "class Program { static int Main() { return 0; } }\n")
        self.assertEqual(rc, 1)
        self.assertIn("Prog.cs:2: delegates are not in the Crust C# subset", out)

    def test_native_attribute_on_a_clean_class_is_native(self):
        self.ok("using Crust;\n[Native] class K { public int n; }\nclass Program { static int Main() { return 0; } }\n")
        self.assertEqual(self.sides()["K"], "native")

    def test_both_attributes_is_an_error(self):
        rc, out = self.build("using Crust;\n[Managed, Native] class K { }\nclass Program { static int Main() { return 0; } }\n")
        self.assertEqual(rc, 1)
        self.assertIn("both [Managed] and [Native]", out)

    def test_a_program_may_declare_its_own_marker_classes(self):
        # attribute classes are matched by name, like [Shared] and [MaxInstances]; they are on both sides
        self.ok("using System;\nclass ManagedAttribute : Attribute { }\n[Managed] class K { public int n; }\n"
                "class Program { static int Main() { return 0; } }\n")
        self.assertEqual(self.sides()["K"], "managed")
        self.assertEqual(self.sides()["ManagedAttribute"], "shared")


class Families(Case):
    SRC = ("interface IShape { int Area(); }\n"
           "class Square : IShape { public int Area() { return 4; } }\n"
           "class Circle : IShape { public int Area() { " + LAMBDA + " } }\n"
           "class Program { static int Main() { return 0; } }\n")

    def test_one_refused_member_makes_the_whole_family_managed(self):
        self.ok(self.SRC)
        self.assertEqual(self.sides(), {"IShape": "managed", "Square": "managed", "Circle": "managed", "Program": "native"})
        sq = [c for c in self.plan()["classes"] if c["name"] == "Square"][0]
        self.assertIn("inheritance family of managed `Circle`", sq["reason"])

    def test_base_class_chain(self):
        self.ok("class A { public int a; }\nclass B : A { public int b; }\n"
                "[Crust.Managed] class C : B { public int c; }\n"
                "class Program { static int Main() { return 0; } }\n")
        self.assertEqual(self.sides(), {"A": "managed", "B": "managed", "C": "managed", "Program": "native"})

    def test_a_native_class_in_a_managed_family_is_an_error(self):
        rc, out = self.build("using Crust;\ninterface I { int V(); }\n"
                             "[Native] class A : I { public int V() { return 1; } }\n"
                             "[Managed] class B : I { public int V() { return 2; } }\n"
                             "class Program { static int Main() { return 0; } }\n")
        self.assertEqual(rc, 1)
        self.assertIn("`A` is marked [Native] but `B` is managed", out)
        self.assertIn("inheritance family", out)
        self.assertEqual(out.count("problem(s)"), 1)
        self.assertIn("1 problem(s)", out)                                   # not also a follow-on reference error

    def test_unrelated_classes_are_not_dragged_along(self):
        self.ok("class A { public int a; }\n[Crust.Managed] class B { public int b; }\n"
                "class Program { static int Main() { return 0; } }\n")
        self.assertEqual(self.sides(), {"A": "native", "B": "managed", "Program": "native"})


class Shared(Case):
    def test_enums_and_attribute_classes_are_on_both_sides(self):
        self.ok("using Crust;\nenum Color { Red, Green }\n"
                "class N { public Color c; }\n"
                "[Managed] class M { public Color c; }\n"
                "class Program { static int Main() { return 0; } }\n")
        s = self.sides()
        self.assertEqual((s["Color"], s["N"], s["M"]), ("shared", "native", "managed"))
        self.assertIn("enum Color", self.managed_text())


class Forms(Case):
    def test_a_delegate_is_managed(self):
        # (a `record` would be too, but the CC# corelib declares no IEquatable<T> / IsExternalInit, so it does not get this far)
        self.ok("delegate int Op(int a);\nclass Program { static int Main() { return 0; } }\n")
        s = self.sides()
        self.assertEqual((s["Op"], s["Program"]), ("managed", "native"))
        self.assertIn("delegate int Op", self.managed_text())

    def test_without_dna_a_delegate_is_still_refused(self):
        rc, out = self.build("delegate int Op(int a);\nclass Program { static int Main() { return 0; } }\n", dna=False)
        self.assertEqual(rc, 1)
        self.assertIn("`delegate` is not in the Crust C# subset", out)


class Bridge(Case):
    """Across the boundary only static methods, with C-sized arguments, are called; the rest is refused once for each pair of classes."""

    def test_a_static_call_across_the_boundary_is_planned(self):
        self.ok("using Crust;\n[Managed] class Script { public static int Go(int n) { return n + 1; } }\n"
                "class Vec { public static int Twice(int a) { return a * 2; } }\n"
                "[Managed] class Driver { public static int Run() { return Vec.Twice(4); } }\n"
                "class Program { static int Main() { return Script.Go(1) + Driver.Run(); } }\n")
        got = {(b["method"], b["direction"], b["signature"]) for b in self.plan()["bridge"]}
        self.assertEqual(got, {("Script.Go", "native->managed", "i>i"), ("Driver.Run", "native->managed", ">i"),
                               ("Vec.Twice", "managed->native", "i>i")})

    def test_the_files_the_bridge_needs_are_written(self):
        self.ok("using Crust;\n[Managed] class Script { public static int Go(int n) { return Vec.Twice(n); } }\n"
                "class Vec { public static int Twice(int a) { return a * 2; } }\n"
                "class Program { static int Main() { return Script.Go(1); } }\n")
        d = os.path.join(self.d, "cpp")
        for f in ("T.bridge.c", "T.ffi.json", os.path.join("managed", "_Bridge.cs")):
            self.assertTrue(os.path.exists(os.path.join(d, f)), f)
        with open(os.path.join(d, "T.main.cpp")) as f:
            main = f.read()
        self.assertIn("ccs_b_Script_Go", main)                       # the proxy the native code calls
        self.assertIn("ccs_x_Vec_Twice", main)                       # the export managed code reaches
        with open(os.path.join(d, "T.ffi.json")) as f:
            self.assertEqual(json.load(f)["functions"][0]["entry"], "ccs_x_Vec_Twice")

    def test_native_code_may_hold_an_object_of_a_managed_class(self):
        self.ok("using Crust;\n[Managed] class Counter { int n; public Counter(int s) { n = s; } public int Add(int k) { n += k; return n; }\n"
                "  public int Count { get { return n; } set { n = value; } } }\n"
                "class Program { static int Main() { Counter c = new Counter(1); c.Count = c.Add(2); return c.Count; } }\n")
        got = {(b["method"], b["kind"], b["direction"], b["signature"]) for b in self.plan()["bridge"]}
        self.assertEqual(got, {("Counter.Counter", "ctor", "native->managed", "i>o"), ("Counter.Add", "instance", "native->managed", "oi>i"),
                               ("Counter.Count", "getter", "native->managed", "o>i"), ("Counter.Count", "setter", "native->managed", "oi>v")})
        d = os.path.join(self.d, "cpp")
        with open(os.path.join(d, "T.main.cpp")) as f:
            main = f.read()
        self.assertIn("~Counter() { ccs_b_release(_ccs_h); }", main)      # the proxy owns the handle, and lets it go at scope exit
        self.assertIn("Counter(int a0) { _ccs_h = ccs_b_Counter__new0(a0); }", main)
        self.assertIn("int get_Count()", main)                              # how Crust spells a property
        with open(os.path.join(d, "managed", "_Bridge.cs")) as f:
            tramp = f.read()
        self.assertIn("internal static int Counter_Add(global::Counter self, int a0) { return self.Add(a0); }", tramp)
        self.assertIn("internal static global::Counter Counter__new0(int a0) { return new global::Counter(a0); }", tramp)

    def test_a_field_of_a_managed_object_is_refused_naming_the_way_out(self):
        rc, out = self.build("using Crust;\n[Managed] class Counter { public int n; public Counter(int s) { n = s; } }\n"
                             "[Native] class Program { static int Main() { Counter c = new Counter(1); return c.n; } }\n")
        self.assertEqual(rc, 1)
        self.assertIn("cannot be reached through a handle", out)
        self.assertIn("Make it a property", out)

    def test_a_managed_class_that_cannot_be_held_says_why(self):
        for decl, why in (("[Managed] class A { } [Managed] class B : A { public int V() { return 1; } }", "a class hierarchy cannot cross the boundary"),
                          ("[Managed] struct B { public int V() { return 1; } }", "only classes can be held by handle")):
            rc, out = self.build("using Crust;\n" + decl + "\n[Native] class Program { static int Main() { B b = new B(); return b.V(); } }\n")
            self.assertEqual(rc, 1, decl)
            self.assertIn(why, out)

    def test_objects_the_other_way_are_one_diagnostic_per_class_pair(self):
        rc, out = self.build("using Crust;\nclass Vec { public int n; public int Get() { return n; } }\n"
                             "[Managed] class Script { public static int Go() { Vec v = new Vec(); return v.Get(); } }\n"
                             "class Program { static int Main() { return Script.Go(); } }\n")
        self.assertEqual(rc, 1)
        self.assertEqual(out.count("as an object"), 1)               # one mistake, one line: not a line for `new`, the variable and the call
        self.assertIn("through their static methods only", out)
        self.assertIn("1 problem(s)", out)

    def test_an_object_the_other_way(self):
        rc, out = self.build("using Crust;\nclass Vec { public int n; public int Twice(int a) { return a * 2; } }\n"
                             "[Managed] class Script { public static int Go() { Vec v = new Vec(); return v.Twice(1); } }\n"
                             "[Managed] class Program { static int Main() { return 0; } }\n")
        self.assertEqual(rc, 1)
        self.assertIn("managed class `Script` uses native class `Vec`", out)
        self.assertIn("as an object", out)

    def test_a_type_that_cannot_cross_is_refused_at_the_use(self):
        rc, out = self.build("using Crust;\n[Managed] class Script { public static int Go(System.Collections.Generic.List<int> l) { return 1; } }\n"
                             "[Native] class Program { static int Main() { System.Collections.Generic.List<int> l = new System.Collections.Generic.List<int>(); return Script.Go(l); } }\n")
        self.assertEqual(rc, 1)
        self.assertIn("parameter `l`", out)
        self.assertIn("cannot cross", out)
        self.assertIn("Allowed: bool, byte", out)                    # and it says what may

    def test_a_managed_entry_point_with_a_native_callee_has_a_glue_main(self):
        self.ok("using Crust;\nclass Vec { public static int Twice(int a) { return a * 2; } }\n"
                "[Managed] class Program { static int Main() { return Vec.Twice(2); } }\n")
        with open(os.path.join(self.d, "cpp", "T.bridge.c")) as f:
            glue = f.read()
        self.assertIn("int main(int argc, char **argv)", glue)
        self.assertIn("DNA_RunMain", glue)


class Demotion(Case):
    """A native class that uses a managed class in a way that cannot cross is managed too: callers follow the classes they use, a callee is never
    pulled across, and [Native] is the way to say no."""

    SRC = ("using System;\nusing Crust;\n"
           "[MaxInstances(4)] class Node { public int Id; public Node(int i) { Id = i; } public void Bump() { Id++; } }\n"
           "[MaxInstances(4)] class Ball { public Node Self; public int Hits;\n"
           "  public void Update() { Func<int, int> f = k => k + Self.Id; Hits = f(Hits); Self.Bump(); } }\n"
           "class Scripts { static Ball[] pool; public static void Init() { pool = new Ball[2]; }\n"
           "  public static Ball Add(Node n) { Ball b = new Ball(); b.Self = n; pool[0] = b; return b; } }\n"
           "class Program { static int Main() { Scripts.Init(); Node n = new Node(1); Ball b = Scripts.Add(n); b.Update(); return b.Hits + n.Id; } }\n")

    def build_src(self, src, **kw):
        # (the marker classes go after the program's own `using` lines: a using clause must come first)
        src = src.replace("using Crust;\n", "")
        marker = "class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }\n"
        if "class ManagedAttribute" not in src:
            marker += "class ManagedAttribute : Attribute { }\n"
        if "class NativeAttribute" not in src:
            marker += "class NativeAttribute : Attribute { }\n"
        lines = src.split("\n")
        at = max([i for i, l in enumerate(lines) if l.startswith("using ")] + [-1]) + 1
        return self.build("\n".join(lines[:at]) + "\n" + marker + "\n".join(lines[at:]), **kw)

    def test_the_engine_stays_native_and_the_game_side_follows_the_script(self):
        rc, out = self.build_src(self.SRC)
        self.assertEqual(rc, 0, out)
        s = self.sides()
        self.assertEqual((s["Node"], s["Ball"], s["Scripts"], s["Program"]), ("native", "managed", "managed", "managed"))
        reasons = {c["name"]: c["reason"] for c in self.plan()["classes"]}
        self.assertIn("follows the managed classes it uses", reasons["Scripts"])
        self.assertIn("Ball", reasons["Scripts"])                                   # and says which
        self.assertEqual(self.plan()["entry"]["side"], "managed")

    def test_only_the_native_class_is_called_from_managed_code(self):
        rc, out = self.build_src(self.SRC)
        self.assertEqual(rc, 0, out)
        who = {b["method"].split(".")[0] for b in self.plan()["bridge"]}
        self.assertEqual(who, {"Node"})                                              # every call across the boundary is on the engine class
        self.assertTrue(all(b["direction"] == "managed->native" for b in self.plan()["bridge"]))

    def test_native_insists_on_staying_native(self):
        rc, out = self.build_src(self.SRC.replace("class Program {", "[Native] class Program {"))
        self.assertEqual(rc, 1)
        self.assertIn("`Program`", out)
        self.assertIn("Ball", out)

    def test_a_chain_of_users_follows_to_the_end(self):
        rc, out = self.build_src("using System;\nusing Crust;\n"
                                 "[Managed] class M { public int v; public int Get() { return v; } }\n"
                                 "class A { public static int Go() { M m = new M(); m.v = 3; return m.Get(); } }\n"
                                 "class B { public static int Go() { return A.Go() + 1; } }\n"
                                 "class Program { static int Main() { return B.Go(); } }\n")
        self.assertEqual(rc, 0, out)
        s = self.sides()
        self.assertEqual((s["A"], s["B"], s["Program"]), ("managed", "native", "native"))   # B only calls a static method of A: that crosses, so B stays

    def test_a_callee_is_never_pulled_across(self):
        rc, out = self.build_src("using System;\nusing Crust;\n"
                                 "class Plain { public int x; public int Get() { return x; } }\n"
                                 "[Managed] class M { public static int Go() { Plain p = new Plain(); p.x = 4; return p.Get(); } }\n"
                                 "class Program { static int Main() { return M.Go(); } }\n")
        self.assertEqual(rc, 1)                                                      # M cannot use an instance of Plain; Plain does not move to M
        self.assertIn("through their static methods only", out)
        self.assertEqual(out.count("problem(s)"), 1)


class ArenaProxies(Case):
    SRC = ("using System;\nclass MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }\nclass ManagedAttribute : Attribute { }\n"
           "[MaxInstances(4)] class Node { public int Id; public float X; public Node Parent; public const int Limit = 4; public static int Made;\n"
           "  public Node(int i) { Id = i; } public void Move(float dx) { X += dx; } public int Depth { get { return Parent == null ? 0 : 1; } }\n"
           "  public void ToWorld(float lx, out float wx) { wx = X + lx; } }\n"
           "[Managed] class Script { public static float Run() { Node a = new Node(1); Node b = new Node(2); b.Parent = a; b.Move(2f);\n"
           "  a.X = 1f; float w; b.ToWorld(1f, out w); return w + b.Depth + Node.Limit + a.Id + a.X; } }\n"
           "class Program { static int Main() { return (int)Script.Run(); } }\n")

    def test_an_arena_object_is_held_by_its_address(self):
        rc, out = self.build(self.SRC, extra=["--dna-corlib=" + ccs_corlib()]) if False else self.build(self.SRC)
        self.assertEqual(rc, 0, out)
        d = os.path.join(self.d, "cpp")
        with open(os.path.join(d, "managed", "_Bridge.cs")) as f:
            cs = f.read()
        self.assertIn("internal long _p;", cs)                                        # the proxy holds only the address
        self.assertIn("Dictionary<long,", cs)                                         # one proxy for each address: `==` and `null` are the C# ones
        self.assertIn("public const int Limit = 4;", cs)                              # a constant is folded by the compiler, so the proxy has its value
        self.assertIn("public int Depth {", cs)                                       # a property
        self.assertIn("public float X {", cs)                                         # a field reads and writes like one
        self.assertIn("out float @wx", cs)
        with open(os.path.join(d, "T.main.cpp")) as f:
            main = f.read()
        self.assertIn("Node * _cb_self = (Node *)h;", main)                          # the export finds the object from its address
        self.assertIn("return (long long)_cb_r;", main)
        with open(os.path.join(d, "T.ffi.json")) as f:
            ffi = json.load(f)
        entries = {fn["entry"]: fn for fn in ffi["functions"]}
        self.assertEqual(entries["ccs_x_Node_Move"]["args"], ["long", "float"])
        self.assertEqual(entries["ccs_x_Node_ToWorld"]["args"], ["long", "float", "ref:float*"])   # `out` is a pointer to a number
        self.assertEqual(entries["ccs_x_Node__new0"]["ret"], "long")

    def test_a_native_arena_object_crosses_to_managed_methods_through_a_trampoline(self):
        rc, out = self.build("using System;\nclass MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }\nclass ManagedAttribute : Attribute { }\n"
                             "[MaxInstances(2)] class Node { public int Id; public Node(int i) { Id = i; } }\n"
                             "[Managed] class Script { public static int Id(Node n) { return n.Id; } }\n"
                             "class Program { static int Main() { Node n = new Node(5); return Script.Id(n); } }\n")
        self.assertEqual(rc, 0, out)
        with open(os.path.join(self.d, "cpp", "managed", "_Bridge.cs")) as f:
            cs = f.read()
        self.assertIn("internal static int Script_Id(long a0) { return global::Script.Id(global::Node.Wrap(a0)); }", cs)
        self.assertEqual({(b["method"], b["signature"]) for b in self.plan()["bridge"] if b["method"] == "Script.Id"}, {("Script.Id", "l>i")})

    def test_a_native_class_that_is_not_an_arena_class_is_still_static_only(self):
        rc, out = self.build("using System;\nclass ManagedAttribute : Attribute { }\nclass Plain { public int x; public int Get() { return x; } }\n"
                             "[Managed] class Script { public static int Go() { Plain p = new Plain(); return p.Get(); } }\n"
                             "class Program { static int Main() { return Script.Go(); } }\n")
        self.assertEqual(rc, 1)
        self.assertIn("through their static methods only", out)


class Entry(Case):
    def test_main_in_a_managed_class_is_a_managed_entry_point(self):
        out = self.ok("using Crust;\nclass Vec { public int x; }\n[Managed] class Program { static int Main() { return 3; } }\n")
        self.assertEqual(self.plan()["entry"], {"side": "managed", "class": "Program"})
        self.assertNotIn("no `static int Main()", out)

    def test_main_in_a_class_that_falls_back_is_a_managed_entry_point(self):
        self.ok("class Program { static int Main() { " + LAMBDA + " } }\n")
        self.assertEqual(self.plan()["entry"], {"side": "managed", "class": "Program"})

    def test_no_main_anywhere_is_still_an_error(self):
        rc, out = self.build("class Vec { public int x; }\n")
        self.assertEqual(rc, 1)
        self.assertIn("no `static int Main()", out)

    def test_two_entry_points_on_two_sides_are_ambiguous(self):
        rc, out = self.build("using Crust;\nclass A { static int Main() { return 0; } }\n"
                             "[Managed] class B { static int Main() { return 1; } }\n")
        self.assertEqual(rc, 1)
        self.assertIn("2 entry points", out)

    def test_main_flag_picks_the_side(self):
        self.ok("using Crust;\nclass A { static int Main() { return 0; } }\n[Managed] class B { static int Main() { return 1; } }\n",
                extra=["--main=B"])
        self.assertEqual(self.plan()["entry"], {"side": "managed", "class": "B"})


class ManagedSource(Case):
    SRC = ("using System;\nusing Crust;\nnamespace Game {\n"
           "  class Vec { public int x; }\n"
           "  [Managed] class Logger { public void Log(int v) { Console.WriteLine(v); } }\n"
           "  class Program { static int Main() { return 0; } }\n}\n")

    def test_native_classes_are_removed_and_crust_attributes_dropped(self):
        self.ok(self.SRC)
        text = self.managed_text()
        self.assertIn("class Logger", text)
        self.assertNotIn("class Vec", text)
        self.assertNotIn("class Program", text)
        self.assertNotIn("[Managed]", text)
        self.assertIn("namespace Game", text)                                # the namespace of what is left is kept

    def test_the_crust_attributes_are_declared_for_the_managed_compile(self):
        self.ok(self.SRC)
        with open(os.path.join(self.d, "cpp", "managed", "_Crust.cs")) as f:
            t = f.read()
        self.assertIn("class ManagedAttribute", t)
        self.assertIn("class NativeAttribute", t)

    def test_line_numbers_are_kept_for_diagnostics(self):
        # a removed native class leaves its newlines behind, so every line of what is left is on its original line
        self.ok(self.SRC)
        with open(os.path.join(self.d, "Prog.cs")) as f:
            orig = f.read().split("\n")
        with open(os.path.join(self.d, "cpp", "managed", "Prog.cs")) as f:
            sliced = f.read().split("\n")
        self.assertEqual(len(sliced), len(orig))
        for i, line in enumerate(sliced):
            if "class Logger" in line:
                self.assertIn("class Logger", orig[i])               # on the line it was on

    def test_all_native_leaves_no_managed_sources(self):
        self.ok("class Program { static int Main() { return 0; } }\n")
        self.assertFalse(os.path.exists(os.path.join(self.d, "cpp", "managed")))


if __name__ == "__main__":
    if not ccs2c.compiler_cmd():
        sys.exit("test_partition: the compiler is not built (python3 build.py compiler)")
    unittest.main(verbosity=1)
