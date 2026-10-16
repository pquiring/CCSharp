#!/usr/bin/env python3
"""test_inputs -- what counts as a C# program: files, folders, .csproj, .sln (crust/inputs.py), and converting
several of them at once.

    python3 crust/test_inputs.py        (python3 build.py test runs it first)
"""
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import inputs                                            # noqa: E402


def write(root, rel, text=""):
    p = os.path.join(root, *rel.split("/"))
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w") as f:
        f.write(text)
    return p


SDK = '<Project Sdk="Microsoft.NET.Sdk">%s</Project>'


class Expand(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.mkdtemp(prefix="ccs-inputs-")

    def tearDown(self):
        shutil.rmtree(self.d, ignore_errors=True)

    def rel(self, files):
        return sorted(os.path.relpath(f, self.d).replace(os.sep, "/") for f in files)

    def test_single_file(self):
        write(self.d, "a.cs")
        write(self.d, "b.cs")
        self.assertEqual(self.rel(inputs.expand(os.path.join(self.d, "a.cs"))), ["a.cs"])

    def test_folder_is_recursive_and_skips_build_output(self):
        for f in ("a.cs", "sub/b.cs", "sub/deep/c.cs", "bin/Debug/x.cs", "obj/y.cs", ".git/z.cs", "sub/obj/q.cs", "readme.txt"):
            write(self.d, f)
        self.assertEqual(self.rel(inputs.expand(self.d)), ["a.cs", "sub/b.cs", "sub/deep/c.cs"])

    def test_several_inputs_are_one_program_sorted_and_unique(self):
        write(self.d, "one/a.cs")
        write(self.d, "two/b.cs")
        write(self.d, "loose.cs")
        got = inputs.expand([os.path.join(self.d, "two"), os.path.join(self.d, "loose.cs"), os.path.join(self.d, "one"),
                             os.path.join(self.d, "one", "a.cs")])
        self.assertEqual(self.rel(got), ["loose.cs", "one/a.cs", "two/b.cs"])

    def test_wildcard(self):
        write(self.d, "src/a.cs")
        write(self.d, "src/deep/b.cs")
        write(self.d, "src/c.txt")
        self.assertEqual(self.rel(inputs.expand(os.path.join(self.d, "src", "**", "*.cs"))), ["src/a.cs", "src/deep/b.cs"])

    def test_sdk_csproj_takes_its_folder_but_not_build_output_or_other_projects(self):
        write(self.d, "App/App.csproj", SDK % "")
        for f in ("App/Main.cs", "App/Models/M.cs", "App/obj/g.cs", "App/bin/g.cs", "App/Other/Other.csproj", "App/Other/O.cs"):
            write(self.d, f, "")
        self.assertEqual(self.rel(inputs.expand(os.path.join(self.d, "App", "App.csproj"))), ["App/Main.cs", "App/Models/M.cs"])

    def test_compile_remove_and_include(self):
        write(self.d, "P/P.csproj", SDK % '<ItemGroup><Compile Remove="Old\\**\\*.cs" /><Compile Remove="Skip.cs" />'
                                          '<Compile Include="..\\Shared\\*.cs" /></ItemGroup>')
        for f in ("P/A.cs", "P/Skip.cs", "P/Old/X.cs", "P/Old/deeper/Y.cs", "Shared/S1.cs", "Shared/S2.cs", "Elsewhere/E.cs"):
            write(self.d, f)
        self.assertEqual(self.rel(inputs.expand(os.path.join(self.d, "P", "P.csproj"))), ["P/A.cs", "Shared/S1.cs", "Shared/S2.cs"])

    def test_default_items_off_means_only_explicit_items(self):
        write(self.d, "P/P.csproj", SDK % '<PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>'
                                          '<ItemGroup><Compile Include="One.cs;Two.cs" /></ItemGroup>')
        for f in ("P/One.cs", "P/Two.cs", "P/Three.cs"):
            write(self.d, f)
        self.assertEqual(self.rel(inputs.expand(os.path.join(self.d, "P", "P.csproj"))), ["P/One.cs", "P/Two.cs"])

    def test_old_style_project_has_no_default_files(self):
        write(self.d, "P/P.csproj", '<Project ToolsVersion="15.0"><ItemGroup><Compile Include="Only.cs" /></ItemGroup></Project>')
        for f in ("P/Only.cs", "P/Other.cs"):
            write(self.d, f)
        self.assertEqual(self.rel(inputs.expand(os.path.join(self.d, "P", "P.csproj"))), ["P/Only.cs"])

    def test_project_references_are_followed_once_even_in_a_cycle(self):
        write(self.d, "A/A.csproj", SDK % '<ItemGroup><ProjectReference Include="..\\B\\B.csproj" /></ItemGroup>')
        write(self.d, "B/B.csproj", SDK % '<ItemGroup><ProjectReference Include="..\\A\\A.csproj" /><ProjectReference Include="..\\C\\C.csproj" /></ItemGroup>')
        write(self.d, "C/C.csproj", SDK % "")
        for f in ("A/a.cs", "B/b.cs", "C/c.cs", "D/unrelated.cs"):
            write(self.d, f)
        self.assertEqual(self.rel(inputs.expand(os.path.join(self.d, "A", "A.csproj"))), ["A/a.cs", "B/b.cs", "C/c.cs"])

    def test_solution_merges_its_projects(self):
        write(self.d, "App/App.csproj", SDK % "")
        write(self.d, "Lib/Lib.csproj", SDK % "")
        write(self.d, "Tests/Tests.csproj", SDK % "")
        for f in ("App/a.cs", "Lib/l.cs", "Tests/t.cs"):
            write(self.d, f)
        sln = write(self.d, "All.sln",
                    'Project("{FAE04EC0}") = "App", "App\\App.csproj", "{1}"\nEndProject\n'
                    'Project("{FAE04EC0}") = "Lib", "Lib\\Lib.csproj", "{2}"\nEndProject\n')
        self.assertEqual(self.rel(inputs.expand(sln)), ["App/a.cs", "Lib/l.cs"])          # Tests is not in the solution

    def test_errors_say_what_is_wrong(self):
        with self.assertRaisesRegex(inputs.InputError, "no such file"):
            inputs.expand(os.path.join(self.d, "nope.cs"))
        write(self.d, "x.txt")
        with self.assertRaisesRegex(inputs.InputError, "don't know how to read"):
            inputs.expand(os.path.join(self.d, "x.txt"))
        os.makedirs(os.path.join(self.d, "empty"))
        with self.assertRaisesRegex(inputs.InputError, "no C# files"):
            inputs.expand(os.path.join(self.d, "empty"))
        write(self.d, "P/P.csproj", SDK % '<ItemGroup><Compile Include="Missing.cs" /></ItemGroup>')
        with self.assertRaisesRegex(inputs.InputError, "not there"):
            inputs.expand(os.path.join(self.d, "P", "P.csproj"))
        with self.assertRaisesRegex(inputs.InputError, "no input"):
            inputs.expand([])

    def test_project_name(self):
        self.assertEqual(inputs.project_name(["/x/My-App.csproj"]), "My_App")
        write(self.d, "proj/src/a.cs")
        self.assertEqual(inputs.project_name([os.path.join(self.d, "proj", "src")]), "proj")


class Convert(unittest.TestCase):
    """several folders and a loose file, converted and run as ONE program."""

    def test_two_folders_and_a_file(self):
        try:
            import ccs2c
            ccs2c.compiler_cmd()
            ccs2c.crust_home()
        except SystemExit:
            self.skipTest("the compiler or crust/coost is not built")
        d = tempfile.mkdtemp(prefix="ccs-multi-")
        try:
            write(d, "core/Counter.cs", "namespace Core { public class Counter { int n; public void Hit() { n++; } public int Get() { return n; } } }\n")
            write(d, "util/Util.cs", "namespace Util { public static class Util { public static int Twice(int x) { return x * 2; } } }\n")
            write(d, "core/Util.cs", "namespace Core { public static class Util { public static int Thrice(int x) { return x * 3; } } }\n")
            write(d, "Main.cs", "using System; using Core;\nclass Prog { public static int Main() { Counter c = new Counter(); c.Hit(); c.Hit();"
                                " Console.WriteLine(Core.Util.Thrice(c.Get()) + Util.Util.Twice(4)); return c.Get(); } }\n")
            ins = [os.path.join(d, "core"), os.path.join(d, "util"), os.path.join(d, "Main.cs")]
            cpp, cpp_dir = ccs2c.to_cpp(ins, name="Multi")
            self.assertTrue(os.path.exists(os.path.join(cpp_dir, "Multi.main.cpp")))
            names = sorted(f for f in os.listdir(cpp_dir) if f.endswith(".cpp"))
            self.assertIn("core_Util.cpp", names)       # two Util.cs in different folders stay two files
            self.assertIn("util_Util.cpp", names)
            rc, out = ccs2c.build_run(ccs2c.to_c(cpp, cpp_dir, "Multi"))
            self.assertEqual((rc, out), (2, "14\n"))
            out_dir = os.path.join(d, "out")
            written = ccs2c.convert(cpp_dir, out_dir, "Multi", ccs2c.to_c(cpp, cpp_dir, "Multi"))
            self.assertTrue(any(w.endswith("Multi.c") for w in written))
            self.assertTrue(any(w.endswith("Multi.main.cpp") for w in written))
        finally:
            shutil.rmtree(d, ignore_errors=True)


class NameCollision(unittest.TestCase):
    def test_a_program_named_like_its_only_file(self):
        """A.cs -> A.cpp, and the program is called A: the aggregate must not overwrite (and include) itself."""
        try:
            import ccs2c
            ccs2c.compiler_cmd()
            ccs2c.crust_home()
        except SystemExit:
            self.skipTest("the compiler or crust/coost is not built")
        d = tempfile.mkdtemp(prefix="ccs-collide-")
        try:
            write(d, "A.cs", "using System;\nclass A { public static int Main(string[] args) { Console.WriteLine(args.Length);"
                              " if (args.Length > 1) Console.WriteLine(args[0] + \"-\" + args[1]); return 3; } }\n")
            cpp, cpp_dir = ccs2c.to_cpp([os.path.join(d, "A.cs")], name="A")
            self.assertIn("A.cpp", os.listdir(cpp_dir))
            self.assertIn("A.main.cpp", os.listdir(cpp_dir))
            c = ccs2c.to_c(cpp, cpp_dir, "A")
            self.assertEqual(ccs2c.build_run(c), (3, "0\n"))
            self.assertEqual(ccs2c.build_run(c, args=("hello", "world")), (3, "2\nhello-world\n"))   # args[i] reads a char *
        finally:
            shutil.rmtree(d, ignore_errors=True)


if __name__ == "__main__":
    unittest.main(verbosity=1)
