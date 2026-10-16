#!/usr/bin/env python3
"""test_direct_c -- CC# writing the C itself, instead of leaving the C++ -> C step to Crust's cpprust.

Every test for that work lives in this one file.

    python3 crust/test_direct_c.py                  run every test
    python3 crust/test_direct_c.py NAME [NAME ..]   run only these: a test's name, with or without its `test_` prefix.
                                                    Shell wildcards work:  'hello*'  '*deterministic'
    python3 crust/test_direct_c.py --list           print the names

Two kinds of test:

  ref_*     pin down the reference: what the cpprust pipeline does today.  They pass now, and keep passing; if one
            breaks, the reference moved and the target below moved with it.
  direct_*  the target: CC# writes C that is the SAME as what cpprust writes, after normalize_c() (below).  Until
            the direct backend exists, `direct_c()` raises NotImplementedError and these tests are reported as
            skipped ("pending"), so they are visible but do not fail the run.
  norm_*    the normalizer itself.  It decides what "the same" means, so it is tested like everything else.

"The same" is deliberately forgiving about two things only, both incidental to cpprust: the numbers in the
temporaries it invents (`_cpp_ret20` is the 20th return it lowered in the whole unit, which depends on what came
before the program), and whitespace.  Everything else, including stray `;` lines, has to match.

C# sources are inline strings, so adding a test is adding one method.
"""
import fnmatch
import functools
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ccs2c                                             # noqa: E402


# ---------------------------------------------------------------------------------------------------- programs

HELLO = '''using System;
class Hello
{
    public static int Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        return 0;
    }
}
'''

EMPTY = '''class A { public static int Main(string[] args) { return 0; } }
'''

# a class with a field, a constructor and methods; an object that is a local value, and calls on it
COUNTER = '''using System;
class Counter
{
    int n;
    public Counter(int start) { n = start; }
    public void Add(int d) { n += d; }
    public int Get() { return n; }
}
class P
{
    public static int Main(string[] args)
    {
        Counter c = new Counter(5);
        c.Add(2);
        Console.WriteLine(c.Get());
        return 0;
    }
}
'''


# recursion, if, for, while: statements that need no objects
CONTROL = """using System;
class Calc
{
    public static int Fib(int n)
    {
        if (n < 2) return n;
        return Fib(n - 1) + Fib(n - 2);
    }
    public static int Sum(int n)
    {
        int s = 0;
        for (int i = 1; i <= n; i++) { s += i; }
        return s;
    }
}
class P
{
    public static int Main(string[] args)
    {
        Console.WriteLine(Calc.Fib(10));
        Console.WriteLine(Calc.Sum(10));
        int k = 3;
        while (k > 0) { Console.Write(k); k--; }
        Console.WriteLine();
        return 0;
    }
}
"""

# bool fields, methods calling each other and `this`'s members, if/else, an object passed to a static method by reference
INSTANCE = """using System;
class Acc
{
    int total;
    int count;
    bool big;
    public Acc() { total = 0; count = 0; }
    public void Add(int v)
    {
        total += v;
        count++;
        if (total > 10) { big = true; }
        else { big = false; }
    }
    public int Twice() { return Get() * 2; }
    public int Get() { return total; }
    public bool IsBig() { return big; }
    public int Count() { return count; }
}
class P
{
    static void Fill(Acc a, int n)
    {
        for (int i = 0; i < n; i++) { a.Add(i); }
    }
    public static int Main(string[] args)
    {
        Acc a = new Acc();
        Fill(a, 6);
        Console.WriteLine(a.Get());
        Console.WriteLine(a.Twice());
        Console.WriteLine(a.Count());
        if (a.IsBig() && a.Count() > 3) Console.WriteLine(1);
        return 0;
    }
}
"""

# field initialisers (all three run before the constructor's own statements), a multi-line constructor, a long field, an object argument
POINT = """using System;
class Pt
{
    int x = 1;
    int y = 2;
    long w = 7;
    public Pt(int a, int b)
    {
        x = a;
        y = b;
    }
    public int Dot(Pt o) { return x * o.x + y * o.y; }
    public int X() { return x; }
}
class P
{
    public static int Main(string[] args)
    {
        Pt p = new Pt(3, 4);
        Pt q = new Pt(5, 6);
        Console.WriteLine(p.Dot(q));
        Console.WriteLine(p.X());
        return 0;
    }
}
"""

# a Main with no arguments and no result: main() has no vector to drop, and the empty parameter list is `(void)`
VOID_MAIN = """using System;
class P
{
    static int Sq(int v) { return v * v; }
    public static void Main()
    {
        Console.WriteLine(Sq(7));
    }
}
"""

# a conditional expression, and Main's result as the exit status
EXIT_CODE = """using System;
class P
{
    public static int Main(string[] args)
    {
        int x = 5;
        int y = x > 3 ? x * 2 : x - 1;
        Console.WriteLine(y);
        return y - 8;
    }
}
"""

# field initialisers and no constructor: cpprust writes a default constructor (last among the members) that runs them; a class with
# nothing to initialise gets none; a bool field and a field with no initialiser
DEFAULT_CTOR = """using System;
class Cfg
{
    int a = 1;
    int b = 2;
    bool on = true;
    int c;
    public int Sum() { return a + b + c; }
    public bool On() { return on; }
}
class Plain
{
    int v;
    public void Set(int x) { v = x; }
    public int Get() { return v; }
}
class P
{
    public static int Main(string[] args)
    {
        Cfg g = new Cfg();
        Plain p = new Plain();
        p.Set(4);
        Console.WriteLine(g.Sum() + p.Get());
        Console.WriteLine(g.On());
        return 0;
    }
}
"""

LIST_INT = """using System;
using System.Collections.Generic;
class Prog {
  static int Total(List<int> xs) { int t = 0; foreach (var x in xs) t += x; return t; }
  public static int Main() {
    List<int> xs = new List<int>();
    for (int i = 1; i <= 4; i++) xs.Add(i * 3);
    xs[1] = 100;
    Console.WriteLine(xs.Count);
    Console.WriteLine(Total(xs));
    return 0;
  }
}
"""

OWNING = """using System;
using System.Collections.Generic;
class Bag {
  public int Cap = 4;
  public int[] Items;
  public string Name;
  public Bag() { Items = new int[Cap]; Name = "bag"; }
  public Bag(int n) { Items = new int[n]; Cap = n; Name = "x"; }
  public int Total() { int t = 0; foreach (int x in Items) t += x; return t; }
}
class Prog {
  static int Main() {
    Bag b = new Bag(3);
    b.Items[1] = 5;
    Bag c = new Bag();
    Console.WriteLine(b.Total() + c.Cap);
    Console.WriteLine(b.Name);
    return 0;
  }
}
"""

VALUES = """using System;
struct Handle {
  public int Index;
  public int Gen;
  public Handle(int i, int g) { Index = i; Gen = g; }
}
class Node { public int V; public Node(int v) { V = v; } }
class Table {
  public int Base = 3;
  public bool Live(Handle h) { return h.Index + Base == h.Gen; }
  public int Sum(Handle a, Handle b) { return a.Index + a.Gen + b.Index + b.Gen; }
}
class Program {
  static Node Make(int v) { return new Node(v * 2); }
  static Node Local(int v) { Node n = new Node(v); n.V += 1; return n; }
  static int Main() {
    Table t = new Table();
    int k = 1;
    Console.WriteLine(t.Live(new Handle(1, 4)) ? 1 : 0);
    Handle second = new Handle(k + 1, 3);
    Console.WriteLine(t.Sum(new Handle(1, 2), second));
    Node n = Make(21);
    Node m = Local(9);
    n = Make(1);
    Console.WriteLine(n.V + m.V);
    return 0;
  }
}
"""

# name -> (C# source, what the program does: (exit status, stdout)).  Each gets two tests: the direct C is the same as cpprust's, and it runs.
PROGRAMS = {
    "empty": (EMPTY, (0, "")),
    "hello": (HELLO, (0, "Hello, World!\n")),
    "counter": (COUNTER, (0, "7\n")),
    "control": (CONTROL, (0, "55\n55\n321\n")),
    "instance": (INSTANCE, (0, "15\n30\n6\n1\n")),
    "point": (POINT, (0, "39\n3\n")),
    "void_main": (VOID_MAIN, (0, "49\n")),
    "exit_code": (EXIT_CODE, (2, "10\n")),
    "default_ctor": (DEFAULT_CTOR, (0, "7\nTrue\n")),
    "owning_fields": (OWNING, (0, "9\nx\n")),
    "values": (VALUES, (0, "1\n8\n12\n")),
    "list_int": (LIST_INT, (0, "4\n124\n")),
}


# ---------------------------------------------------------------------------------------------------- pipelines

def _need_toolchain():
    """Skip (not fail) where the compiler, crust or coost is not built: the same rule test_inputs.py uses."""
    try:
        ccs2c.compiler_cmd()
        ccs2c.crust_home()
    except SystemExit:
        raise unittest.SkipTest("the compiler or crust/coost is not built (python3 build.py)")


def _write_program(d, source, name):
    cs = os.path.join(d, name + ".cs")
    with open(cs, "w") as f:
        f.write(source)
    work = os.path.join(d, "work")
    os.makedirs(work, exist_ok=True)
    return cs, work


def reference_cpp(source, name="P"):
    """The C++ subset CC# writes for `source`: (cpp text, directory of generated files, scratch directory to remove)."""
    d = tempfile.mkdtemp(prefix="ccs-direct-")
    cs, work = _write_program(d, source, name)
    cpp, cpp_dir = ccs2c.to_cpp([cs], name=name, workdir=work)
    return cpp, cpp_dir, d


@functools.lru_cache(maxsize=None)
def reference_c(source, name="P"):
    """The C that cpprust makes for `source`: the thing the direct backend has to equal.  Cached for the run: it costs ~2 s."""
    cpp, cpp_dir, d = reference_cpp(source, name)
    try:
        return ccs2c.to_c(cpp, cpp_dir, name)
    finally:
        shutil.rmtree(d, ignore_errors=True)


# cpprust's numbered temporaries: _cpp_ret20, _cpp_mv3, __cpp_op7, _cpp_h_2, _cpp_done_2 ...  (each family has its own counter)
_TEMP = re.compile(r"\b(__?cpp_[A-Za-z]+_?)(\d+)\b")


def normalize_c(text):
    """What 'the same C' means.  Renumber each family of cpprust temporaries in order of first appearance, strip trailing
    whitespace, and drop blank lines (they are only padding: cpprust keeps the C# source's line numbers).  Nothing else is touched."""
    seen = {}

    def renumber(m):
        fam, n = m.group(1), m.group(2)
        ids = seen.setdefault(fam, {})
        return "%s%d" % (fam, ids.setdefault(n, len(ids) + 1))
    text = _TEMP.sub(renumber, text)
    lines = [l.rstrip() for l in text.replace("\r\n", "\n").split("\n")]
    return "".join(l + "\n" for l in lines if l != "")


SLOT = "CcsSlot"                          # the one empty class the lowerer puts in the program's place (compiler/src/CppLower.cs)
_SECTION = re.compile(r"^@@(\w+)\n", re.M)


def lower_cpp(cpp_dir, name, foreign=None):
    """Run the C# compiler's direct lowerer on the program's aggregate: {section name: text}.  Phase 1 (no `foreign`) gives only the
    skeleton; phase 2 gets cpprust's C for the surroundings, from which it reads what the library types are called and how they are
    called, and gives all the sections.  NotImplementedError when it says it does not handle some construct yet (exit status 3) -- the
    test is then pending, not failed."""
    cmd = ccs2c.compiler_cmd() + ["--lower-cpp", os.path.join(cpp_dir, name + ".main.cpp")]
    if foreign:
        cmd.append("--foreign=" + foreign)
    p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if p.returncode == 3:
        raise NotImplementedError(p.stderr.decode("utf-8", "replace").strip())
    if p.returncode != 0:
        raise RuntimeError("the direct lowerer failed (%d):\n%s" % (p.returncode, p.stderr.decode("utf-8", "replace")))
    out = p.stdout.decode("utf-8", "replace")
    parts = _SECTION.split(out)                       # ['', name, text, name, text ...]
    return {parts[i]: parts[i + 1] for i in range(1, len(parts) - 1, 2)}


@functools.lru_cache(maxsize=None)
def skeleton_c(skeleton):
    """cpprust's C for the program's surroundings: the aggregate with the program's types replaced by one empty slot class.  Everything the
    program does not write -- the standard-library instances, and the coost sources its headers reach -- is in it.  Cached by its text.
    Goes away when CC# lowers those itself (step 2)."""
    d = tempfile.mkdtemp(prefix="ccs-skel-")
    try:
        with open(os.path.join(d, "P.main.cpp"), "w") as f:
            f.write(skeleton)
        return ccs2c.to_c(skeleton, d, "P")
    finally:
        shutil.rmtree(d, ignore_errors=True)


def vector_pieces(skel_c):
    """The skeleton instantiates std::vector<Name> (and std::vector<Name *>, for an arena class) for each program class a vector holds,
    against a stand-in class CcsE_Name, because the instance's text is cpprust's.  Take those instances out of the skeleton's C:
    (the C without the stand-ins and their instances, {key: (fwd, proto, defs, struct)}), the key being Name, or Name_P for the pointer
    instance (whose struct line cpprust writes before the class, apart from its functions).  The program's sections place them (see the
    markers in CppLower.cs)."""
    c = skel_c
    pieces = {}
    for n in re.findall(r"^struct CcsE_(\w+);$", c, re.M):
        x = "CcsE_" + n
        stub = "struct %s;\ntypedef struct %s %s;\n" % (x, x, x)
        if stub not in c:
            raise RuntimeError("the skeleton's C has no forward declaration of the stand-in of " + n)
        c = c.replace(stub, "", 1)
        c = re.sub(r"^static [^\n]*(?<![\w])%s_\w+\([^\n]*\);\n" % x, "", c, flags=re.M)
        last_end = 0
        found = False
        for suffix in ("", "_P"):
            v = "vector_" + x + suffix
            fwd = "struct %s;\ntypedef struct %s %s;\n" % (v, v, v)
            if fwd not in c:
                continue
            found = True
            c = c.replace(fwd, "", 1)
            vproto = re.compile(r"^static [^\n]*\b%s_\w+\([^\n]*\);\n" % v, re.M)
            proto = "".join(m.group(0) for m in vproto.finditer(c))
            c = vproto.sub("", c)
            m = re.search(r"static inline [^\n]*%s_rend\([^\n]*\) \{[^\n]*\}\n" % v, c)
            start = c.index("struct %s {" % v)
            defs = c[start:m.end()]
            struct = ""
            if suffix == "_P":                                  # the struct line is written ahead of the class; the functions follow it
                struct, defs = defs.split("\n", 1)
                struct += "\n"
                c = c.replace(struct, "", 1)
            else:
                last_end = max(last_end, m.end())
            key = n + suffix
            pieces[key] = tuple(t.replace(x, n) for t in (fwd, proto, defs, struct))
            if suffix == "_P":
                # the functions of the pointer instance sit after the class, inside the region removed below
                m2 = re.search(r"static inline [^\n]*%s_rend\([^\n]*\) \{[^\n]*\}\n" % v, c)
                last_end = max(last_end, m2.end())
        if not found:
            raise RuntimeError("the skeleton's C has no vector instance for the stand-in of " + n)
        i = c.index("struct %s {" % x)
        j = c.index("\n;\n", max(last_end, i)) + 3
        c = c[:i] + c[j:]
    return c, pieces


def splice(skel_c, sec):
    """Put the program's three sections where the slot class's three pieces are in cpprust's C for the skeleton."""
    skel_c, vecs = vector_pieces(skel_c)
    sec = dict(sec)
    for k, idx, tag in (("fwd", 0, "FWD"), ("proto", 1, "PROTO"), ("defs", 2, "DEFS")):
        sec[k] = re.sub(r"/\*CCS-VEC-%s:(\w+)\*/\n" % tag, lambda m: vecs[m.group(1)][idx], sec[k])
    sec["defs"] = re.sub(r"/\*CCS-VEC-STRUCT:(\w+)\*/\n", lambda m: vecs[m.group(1)][3], sec["defs"])
    fwd = "struct %s;\ntypedef struct %s %s;\n" % (SLOT, SLOT, SLOT)
    if fwd not in skel_c:
        raise RuntimeError("the skeleton's C has no forward declaration of %s" % SLOT)
    c = skel_c.replace(fwd, sec["fwd"], 1)
    protos = list(re.finditer(r"^static [^\n]*\b%s_\w+\([^\n]*\);\n" % SLOT, c, re.M))      # Main, and the probe the lowerer adds for `bool`
    if not protos:
        raise RuntimeError("the skeleton's C has no prototype of %s_Main" % SLOT)
    for m in reversed(protos[1:]):
        c = c[:m.start()] + c[m.end():]
    c = c[:protos[0].start()] + sec["proto"] + c[protos[0].end():]
    i = c.index("struct %s {" % SLOT)                 # the slot's definition, and main after it...
    j = c.index("int main(int argc, char **argv) {", i)
    k = c.index("{", j)
    depth = 0
    for e in range(k, len(c)):                        # ...to the closing brace of main; what cpprust wrote after it (coost's out-of-line bodies) stays
        depth += (c[e] == "{") - (c[e] == "}")
        if depth == 0:
            break
    tail = c[e + 1:]
    shift = len(set(re.findall(r"__CCS_RET(\d+)__", sec["defs"])))      # the program's returns come before the tail's in cpprust's numbering
    tail = re.sub(r"\b_cpp_ret(\d+)\b", lambda m: "_cpp_ret%d" % (int(m.group(1)) + shift), tail)
    head = c[:i]
    # cpprust numbers its return temporaries across the unit: the program's come after everything lowered before it
    base = max([int(n) for n in re.findall(r"\b_cpp_ret(\d+)\b", head)] or [0])
    defs = re.sub(r"__CCS_RET(\d+)__", lambda m: "_cpp_ret%d" % (base + int(m.group(1))), sec["defs"])
    heap = "/*CCS-HEAP*/\n" in defs
    defs = defs.replace("/*CCS-HEAP*/\n", "")
    out = head + defs + tail
    if heap:
        out = "void *malloc(unsigned long);\nvoid free(void *);\n" + out
    return out


def direct_c(source, name="P"):
    """The C that CC# writes for `source` with its own lowerer for the program's part (compiler/src/CppLower.cs); cpprust lowers only the
    surroundings, once per distinct set of headers.  NotImplementedError where the lowerer does not handle the program yet."""
    cpp, cpp_dir, d = reference_cpp(source, name)
    try:
        skel = skeleton_c(lower_cpp(cpp_dir, name)["skeleton"])                       # phase 1: the surroundings
        foreign = os.path.join(d, "surroundings.c")
        with open(foreign, "w") as f:
            f.write(skel)
        sec = lower_cpp(cpp_dir, name, foreign=foreign)                               # phase 2: the program, knowing what the library types are
    finally:
        shutil.rmtree(d, ignore_errors=True)
    return splice(skel, sec)


# ---------------------------------------------------------------------------------------------------- the reference

class Reference(unittest.TestCase):
    """What the cpprust pipeline does today."""

    @classmethod
    def setUpClass(cls):
        _need_toolchain()

    def test_ref_hello_runs(self):
        """C# -> C++ -> cpprust -> C -> gcc runs, and prints what C# prints."""
        c = reference_c(HELLO)
        self.assertEqual(ccs2c.build_run(c), (0, "Hello, World!\n"))

    def test_ref_lowering_is_deterministic(self):
        """The same C++ lowers to the same C every time.  Without this, comparing the direct backend byte for byte means nothing."""
        cpp, cpp_dir, d = reference_cpp(HELLO)
        try:
            first = ccs2c.to_c(cpp, cpp_dir, "P")
            second = ccs2c.to_c(cpp, cpp_dir, "P")
        finally:
            shutil.rmtree(d, ignore_errors=True)
        self.assertEqual(first, second)

    def test_ref_normalized_c_still_runs(self):
        """normalize_c is idempotent on real cpprust output, and the normalized C compiles and behaves the same: 'the same' stays meaningful."""
        c = reference_c(HELLO)
        n = normalize_c(c)
        self.assertEqual(normalize_c(n), n)
        self.assertEqual(ccs2c.build_run(n), (0, "Hello, World!\n"))

    def test_ref_program_names_survive(self):
        """The program's own names are in the C as cpprust mangles them: `class Hello { Main }` becomes `Hello_Main`."""
        c = reference_c(HELLO)
        self.assertIn("struct Hello {", c)
        self.assertIn("static int Hello_Main(vector_const_char_P *args)", c)
        self.assertIn("{ int _cpp_ret", c)               # a `return` with a live destructor drops it first, in one block


# ---------------------------------------------------------------------------------------------------- the normalizer

class Normalize(unittest.TestCase):
    """normalize_c: needs no toolchain."""

    def test_norm_renumbers_temps_by_first_appearance(self):
        a = "{ int _cpp_ret20 = (0); return _cpp_ret20; }\n{ int _cpp_ret21 = (1); return _cpp_ret21; }\n"
        b = "{ int _cpp_ret3 = (0); return _cpp_ret3; }\n{ int _cpp_ret9 = (1); return _cpp_ret9; }\n"
        self.assertEqual(normalize_c(a), normalize_c(b))
        self.assertIn("_cpp_ret1", normalize_c(a))
        self.assertIn("_cpp_ret2", normalize_c(a))

    def test_norm_families_count_separately(self):
        a = "_cpp_ret5 _cpp_mv7 __cpp_op2 _cpp_h_4 _cpp_done_4\n"
        b = "_cpp_ret1 _cpp_mv1 __cpp_op1 _cpp_h_1 _cpp_done_1\n"
        self.assertEqual(normalize_c(a), normalize_c(b))

    def test_norm_keeps_a_real_difference_in_how_temps_are_shared(self):
        """Renumbering must not make `x x` and `x y` look alike: it is a bijection, not a wildcard."""
        self.assertNotEqual(normalize_c("_cpp_ret5 _cpp_ret5\n"), normalize_c("_cpp_ret5 _cpp_ret6\n"))

    def test_norm_ignores_blank_lines_and_trailing_spaces(self):
        self.assertEqual(normalize_c("int a;  \n\n\n\nint b;\t\n\n"), normalize_c("\nint a;\n\nint b;\n"))
        self.assertEqual(normalize_c("int a;\nint b;\n"), normalize_c("int a;\n\n\nint b;\n"))      # no blank line at all is the same as some

    def test_norm_keeps_stray_semicolons_and_real_text(self):
        self.assertNotEqual(normalize_c("int a;\n;\n"), normalize_c("int a;\n"))
        self.assertNotEqual(normalize_c("int a;\n"), normalize_c("int b;\n"))

    def test_norm_leaves_ordinary_names_alone(self):
        self.assertEqual(normalize_c("struct _cs_object { char _unused; };\nint x2;\n"), "struct _cs_object { char _unused; };\nint x2;\n")


# ---------------------------------------------------------------------------------------------------- the target

class Direct(unittest.TestCase):
    """CC# writes the C itself, and it is the same C."""

    @classmethod
    def setUpClass(cls):
        _need_toolchain()

    def same_as_cpprust(self, source, name="P"):
        try:
            got = direct_c(source, name)
        except NotImplementedError as e:
            self.skipTest("pending: %s" % e)
        want = reference_c(source, name)
        self.assertEqual(normalize_c(got), normalize_c(want))

    def runs_like_csharp(self, source, want, name="P"):
        """The direct C compiles with gcc and behaves as the C# does: (exit status, stdout)."""
        try:
            got = direct_c(source, name)
        except NotImplementedError as e:
            self.skipTest("pending: %s" % e)
        self.assertEqual(ccs2c.build_run(got), want)


def _add_program_tests():
    for pname, (source, want) in PROGRAMS.items():
        def matches(self, source=source):
            self.same_as_cpprust(source)

        def runs(self, source=source, want=want):
            self.runs_like_csharp(source, want)
        matches.__doc__ = "%s: the direct C is the same as cpprust's, after normalize_c" % pname
        runs.__doc__ = "%s: the direct C compiles with gcc and behaves as the C# does" % pname
        setattr(Direct, "test_direct_%s_matches_cpprust" % pname, matches)
        setattr(Direct, "test_direct_%s_runs" % pname, runs)


_add_program_tests()


# ---------------------------------------------------------------------------------------------------- the repository's own cases

CASES = os.path.join(HERE, "tests")


class Corpus(unittest.TestCase):
    """Every single-file program in crust/tests (the cases build.py test compares with real .NET): the direct C must be the same as cpprust's.
    A case the lowerer does not handle yet is skipped as pending with its reason, so the count of skips is the work left."""

    @classmethod
    def setUpClass(cls):
        _need_toolchain()


def _corpus_names():
    names = []
    for f in sorted(os.listdir(CASES)):
        if not f.endswith(".cs") or f.startswith(("refuse_", "dna_")):
            continue
        with open(os.path.join(CASES, f)) as fh:
            head = fh.read()
        if "// dna" in head or "// main:" in head:               # built with other options (--dna, --main)
            continue
        names.append(f[:-3])
    return names


def _add_corpus_tests():
    for cname in _corpus_names():
        def case(self, cname=cname):
            with open(os.path.join(CASES, cname + ".cs")) as fh:
                source = fh.read()
            try:
                got = direct_c(source)
            except NotImplementedError as e:
                self.skipTest("pending: %s" % e)
            self.assertEqual(normalize_c(got), normalize_c(reference_c(source)))
        case.__doc__ = "crust/tests/%s.cs: the direct C is the same as cpprust's" % cname
        setattr(Corpus, "test_corpus_%s" % cname, case)


_add_corpus_tests()


# ---------------------------------------------------------------------------------------------------- running

def _all_tests():
    loader = unittest.TestLoader()
    for case in (Normalize, Reference, Direct, Corpus):
        for t in loader.loadTestsFromTestCase(case):
            yield t


def _short(test):
    n = test._testMethodName
    return n[5:] if n.startswith("test_") else n


def _selected(patterns):
    tests = list(_all_tests())
    if not patterns:
        return tests, []
    chosen, missing = [], []
    for p in patterns:
        p = p[5:] if p.startswith("test_") else p
        hit = [t for t in tests if fnmatch.fnmatchcase(_short(t), p)]
        if not hit:
            missing.append(p)
        chosen += [t for t in hit if t not in chosen]
    return chosen, missing


def main(argv):
    if "-h" in argv or "--help" in argv:
        print(__doc__)
        return 0
    if "--list" in argv:
        for t in _all_tests():
            print(_short(t))
        return 0
    chosen, missing = _selected(argv)
    if missing:
        print("no such test: %s   (python3 crust/test_direct_c.py --list)" % ", ".join(missing))
        return 2
    suite = unittest.TestSuite(chosen)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
