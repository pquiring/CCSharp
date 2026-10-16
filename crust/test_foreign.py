#!/usr/bin/env python3
"""test_foreign -- a program may declare a foreign C function: an `extern` member with a [Crust.Cpp] template (and [Crust.CppInclude] for the
header). Its declaration emits nothing; each call spells the template. Without a template an `extern` is still refused.

    python3 crust/test_foreign.py        (python3 build.py test runs it)
"""
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ccs2c                                             # noqa: E402


class Foreign(unittest.TestCase):
    def run_cs(self, src):
        d = tempfile.mkdtemp(prefix="ccs-foreign-")
        try:
            p = os.path.join(d, "Prog.cs")
            with open(p, "w") as f:
                f.write(src)
            cpp, cpp_dir = ccs2c.to_cpp([p], None, name="T")
            return ccs2c.build_run(ccs2c.to_c(cpp, cpp_dir, "T"))
        finally:
            shutil.rmtree(d, ignore_errors=True)

    def test_a_foreign_function_is_called_through_its_template(self):
        rc, out = self.run_cs(
            "using System;\n"
            "static class Libc {\n"
            "  [Crust.Cpp(\"abs({0})\"), Crust.CppInclude(\"<stdlib.h>\")] public static extern int Abs(int x);\n"
            "  [Crust.Cpp(\"labs({0})\"), Crust.CppInclude(\"<stdlib.h>\")] public static extern long Labs(long x);\n"
            "}\n"
            "class Program { static int Main() { Console.WriteLine(Libc.Abs(-41)); Console.WriteLine(Libc.Labs(-5000000000L)); return Libc.Abs(-3); } }\n")
        self.assertEqual((rc, out), (3, "41\n5000000000\n"))

    def test_a_foreign_function_with_an_array_and_a_string(self):
        rc, out = self.run_cs(
            "using System;\n"
            "static class Libc {\n"
            "  [Crust.Cpp(\"strlen({0:c})\"), Crust.CppInclude(\"<string.h>\")] public static extern int Len(string s);\n"
            "}\n"
            "class Program { static int Main() { Console.WriteLine(Libc.Len(\"hello\")); return 0; } }\n")
        self.assertEqual((rc, out), (0, "5\n"))

    def test_an_extern_without_a_template_is_still_refused(self):
        with self.assertRaises(ccs2c.Refused) as cm:
            self.run_cs("static class Libc { public static extern int Abs(int x); }\n"
                        "class Program { static int Main() { return Libc.Abs(1); } }\n")
        self.assertIn("`extern` methods are not in the Crust C# subset", str(cm.exception))


class ForeignFromManaged(unittest.TestCase):
    """Managed code (on DotNetAnywhere) calls a foreign C function that a native class declares with a [Cpp] template: the export spells the template."""

    def setUp(self):
        have, why = ccs2c.dna_available()
        if not have:
            self.skipTest("--dna: " + why)

    def run_dna(self, src):
        d = tempfile.mkdtemp(prefix="ccs-foreign-dna-")
        self.addCleanup(shutil.rmtree, d, True)
        p = os.path.join(d, "Prog.cs")
        with open(p, "w") as f:
            f.write(src)
        cpp, cpp_dir = ccs2c.to_cpp([p], None, name="T", dna=True)
        return ccs2c.build_run_dna(ccs2c.to_c(cpp, cpp_dir, "T"), cpp_dir, "T"), cpp_dir

    def test_a_managed_class_calls_a_foreign_function_and_reads_its_constant(self):
        (rc, out), cpp_dir = self.run_dna(
            "using System;\n"
            "class ManagedAttribute : Attribute { }\n"
            "static class Libc {\n"
            "  public const int Eight = 8;\n"
            "  [Crust.Cpp(\"abs({0})\"), Crust.CppInclude(\"<stdlib.h>\")] public static extern int Abs(int x);\n"
            "  [Crust.Cpp(\"strlen({0:c})\"), Crust.CppInclude(\"<string.h>\")] public static extern int Len(string s);\n"
            "}\n"
            "[Crust.Managed] class Script { public static int Run() { Console.WriteLine(Libc.Abs(-41)); Console.WriteLine(Libc.Len(\"hello\")); return Libc.Eight; } }\n"
            "class Program { static int Main() { return Script.Run(); } }\n")
        self.assertEqual((rc, out), (8, "41\n5\n"))
        with open(os.path.join(cpp_dir, "T.main.cpp")) as f:
            main = f.read()
        self.assertIn("abs(a0)", main)                                    # the template, with the export's own argument
        self.assertIn("strlen(a0)", main)                                 # `{0:c}` is the C string the export was given


if __name__ == "__main__":
    unittest.main(verbosity=1)
