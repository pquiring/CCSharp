#!/usr/bin/env python3
"""test_arena -- what .NET cannot be the oracle for: an arena class (`[MaxInstances(N)] class T`) has N slots and the (N+1)th live object ABORTS
(in .NET the program would simply go on); what the emitter writes for an arena class; foreign headers and types.

    python3 crust/test_arena.py        (python3 build.py test runs it)
"""
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ccs2c                                             # noqa: E402
import unit                                              # noqa: E402

ATTR = "class MaxInstancesAttribute : System.Attribute { public MaxInstancesAttribute(int n) { } }\n"


class Arena(unittest.TestCase):
    def build(self, src):
        d = tempfile.mkdtemp(prefix="ccs-arena-")
        self.addCleanup(shutil.rmtree, d, True)
        p = os.path.join(d, "Prog.cs")
        with open(p, "w") as f:
            f.write(src)
        cpp, cpp_dir = ccs2c.to_cpp([p], None, name="T")
        return cpp, cpp_dir, ccs2c.to_c(cpp, cpp_dir, "T")

    def test_the_class_carries_its_capacity_and_a_reference_is_a_pointer(self):
        cpp, cpp_dir, c = self.build(ATTR + "[MaxInstances(7)] class Node { public int V; public Node Next; }\n"
                                     "class Program { static int Main() { Node n = new Node(); n.Next = null; return n.Next == null ? 0 : 1; } }\n")
        with open(os.path.join(cpp_dir, "Prog.cpp")) as f:
            text = f.read()
        self.assertIn("static const int __max_instances = 7;", text)
        self.assertIn("Node * n = new Node()", text)
        self.assertIn("n->Next = NULL", text)
        self.assertIn("Node__alloc", c)                                   # cpprust turned `new` into the arena's allocator

    def test_the_marker_class_is_not_emitted(self):
        _, cpp_dir, c = self.build(ATTR + "[MaxInstances(2)] class Node { public int V; }\nclass Program { static int Main() { return 0; } }\n")
        with open(os.path.join(cpp_dir, "Prog.cpp")) as f:
            self.assertNotIn("MaxInstancesAttribute", f.read())

    def test_within_capacity_runs(self):
        _, _, c = self.build(ATTR + "[MaxInstances(3)] class Node { public int V; public Node(int v) { V = v; } }\n"
                             "class Program { static int Main() { int t = 0; for (int i = 0; i < 3; i++) { Node n = new Node(i + 1); t += n.V; }\n"
                             "  System.Console.WriteLine(t); return 0; } }\n")
        self.assertEqual(ccs2c.build_run(c), (0, "6\n"))

    def test_one_object_too_many_aborts_rather_than_overwrite_another(self):
        _, _, c = self.build(ATTR + "[MaxInstances(3)] class Node { public int V; public Node(int v) { V = v; } }\n"
                             "class Program { static int Main() { for (int i = 0; i < 4; i++) { Node n = new Node(i); System.Console.WriteLine(i); }\n"
                             "  System.Console.WriteLine(99); return 0; } }\n")
        rc, out = ccs2c.build_run(c)
        self.assertNotEqual(rc, 0)                                        # killed (abort), not a quiet overwrite
        self.assertNotIn("99", out)                                       # the program did not go on

    def test_a_list_of_references_holds_the_same_objects(self):
        _, _, c = self.build("using System.Collections.Generic;\n" + ATTR +
                             "[MaxInstances(4)] class Item { public int V; }\n"
                             "class Program { static int Main() { List<Item> a = new List<Item>(); Item x = new Item(); a.Add(x); a.Add(x);\n"
                             "  x.V = 5; System.Console.WriteLine(a[0].V + a[1].V); return a[0] == a[1] ? 0 : 1; } }\n")
        self.assertEqual(ccs2c.build_run(c), (0, "10\n"))


class Foreign(unittest.TestCase):
    def test_a_foreign_include_goes_above_what_cpprust_generated(self):
        c = ('#include <stdint.h>\n#include <stdbool.h>\nstruct vector_X;\nstatic inline void vector_X_push(struct vector_X *t, FTransform v);\n'
             'static int f(void) { return 1; }\n#include "foreign.h"\n#include "other.h"\n#include "foreign.h"\n')
        out = unit.hoist_foreign_includes(c)
        self.assertEqual(out.count('#include "foreign.h"'), 1)            # once, even if it was written twice
        self.assertLess(out.index('#include "foreign.h"'), out.index("vector_X_push"))
        self.assertLess(out.index('#include "other.h"'), out.index("vector_X_push"))
        self.assertGreater(out.index('#include "foreign.h"'), out.index("#include <stdbool.h>"))
        self.assertNotIn('static int f(void) { return 1; }\n#include', out)

    def test_no_foreign_include_is_no_change(self):
        c = "#include <stdint.h>\nint f(void);\n"
        self.assertEqual(unit.hoist_foreign_includes(c), c)

    def test_a_type_named_with_cpp_is_not_emitted_as_a_class(self):
        d = tempfile.mkdtemp(prefix="ccs-foreign-")
        self.addCleanup(shutil.rmtree, d, True)
        p = os.path.join(d, "Prog.cs")
        with open(p, "w") as f:
            f.write("namespace Lib {\n"
                    "  [Crust.Cpp(\"div_t\"), Crust.CppInclude(\"<stdlib.h>\")] public struct DivT { [Crust.Cpp(\"{this}.quot\")] public int quot; "
                    "[Crust.Cpp(\"{this}.rem\")] public int rem; }\n}\n"
                    "class Program { static int Main() { return 0; } }\n")
        cpp, cpp_dir = ccs2c.to_cpp([p], None, name="T")
        out = [os.path.join(cpp_dir, n) for n in os.listdir(cpp_dir) if n.endswith(".cpp") and "Prog" in n]
        text = ""
        for o in out:
            with open(o) as f:
                text += f.read()
        self.assertNotIn("Lib_DivT", text)                                # the C type is declared by its header, not by a shadow class


if __name__ == "__main__":
    unittest.main(verbosity=1)
