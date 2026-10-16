#!/usr/bin/env python3
"""build.py -- build CC# (C# -> the C++ subset of Crust) and the two things it requires, offline; and use it.

CC# needs two sibling checkouts, beside this repository:

    ../crust    https://github.com/brentharts/crust   cpprust (C++ subset -> C); its shivyc is an optional C compiler
    ../coost    https://github.com/crustos/coost      the string / fs / path / time library the corelib sits on

and, for --dna (and --wasm, which implies it), a third:

    ../DotNetAnywhere   https://github.com/crustos/DotNetAnywhere   a .NET runtime in C: runs the classes Crust cannot lower
                                                                    (the version with native/src/Host.h; also needs mono-mcs)

BUILD
    python3 build.py                  deps, compiler, coost, corelib, test, examples
    python3 build.py deps             clone crust and coost beside this repo (the only step that needs a network)
    python3 build.py deps --dna       ... and DotNetAnywhere too (--wasm needs it as well)
    python3 build.py compiler         build build/compiler/ccs.dll with the Roslyn inside the .NET SDK (no NuGet)
    python3 build.py coost            build coost with its own build.py (`--test` also runs coost's tests)
    python3 build.py corelib          check the corelib: the C# compiles, the native helpers lower and compile
    python3 build.py test [names..]   input tests, then every case in crust/tests vs real .NET (gcc)
    python3 build.py examples         build and run examples/*
    python3 build.py status | clean

USE   INPUT is any mix of C# files, folders, .csproj and .sln: together they are ONE program
    python3 build.py convert INPUT.. [-o DIR] [--c]     write the generated C++ (and with --c, one self-contained C file)
    python3 build.py compile INPUT.. [-o EXE]           build a native executable (default build/bin/NAME)
    python3 build.py run     INPUT.. [-- ARGS..]        compile and run it; ARGS go to the program

    python3 build.py run examples/example1/src
    python3 build.py compile src/ extra/Helpers.cs -o app
    python3 build.py compile MyApp/MyApp.csproj --main MyApp.Program      (its ProjectReferences come along)
    python3 build.py convert Everything.sln -o generated --c

OPTIONS
    -o PATH          the output: executable (compile) or folder (convert)
    --main CLASS     the class whose static Main is the entry point (needed if there are several)
    --name NAME      the program's name (default: from the first input)
    --c              convert: also write the lowered C
    --cc CC          the C compiler (default: $CC, else cc / gcc)
    --debug          compile with -g -O0 instead of -O2
    --shivyc         use Crust's own compiler (shivyc) instead of gcc.  Experimental: see README.md.
                     With `test` (or no command): also compile each test case with shivyc and require agreement.
    --dna            a class Crust cannot lower (or marked [Managed]) stays C# and runs on DotNetAnywhere, called from the native code
                     and calling it (static methods, for now).  With `test`: the --dna cases are skipped if DotNetAnywhere or mcs is missing.
    --wasm           build for WebAssembly (wasm32-wasi) instead of for this machine: compile/run make NAME.wasm and a launcher NAME that runs it under
                     node, with run_wasm.mjs, NAME.managed.dll and corlib.dll beside them.  Implies --dna (a class Crust cannot lower runs on
                     DotNetAnywhere, with its compiler from CIL to wasm, inside the same module).  Needs clang, lld, wasi-libc, llvm-ar, node and mono-mcs.
                     With `test`: run every case this way (each is compared with real .NET all the same).
    --offline        never touch the network: a missing dependency is an error that says what to clone
    --update         deps: `git pull --ff-only` the dependencies
    --no-test        with no command: skip the test step
    --test           coost: also run coost's own tests

ENVIRONMENT   CRUST_HOME, COOST_HOME, DNA_HOME (dependency locations), DOTNET (the dotnet executable), CC (the C compiler),
              WASI_SYSROOT (wasi-libc, for --wasm; default /usr)
NEEDS         python3, git (deps only), a C compiler, and the .NET SDK 8+ (its Roslyn compiles the compiler)
"""
import glob
import hashlib
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
SIBLINGS = os.path.dirname(ROOT)
BUILD = os.path.join(ROOT, "build")
COMPILER_DIR = os.path.join(BUILD, "compiler")
COMPILER_DLL = os.path.join(COMPILER_DIR, "ccs.dll")

DEPS = {
    "crust": ("https://github.com/brentharts/crust.git", os.path.join("tools", "cpprust.py")),
    "coost": ("https://github.com/crustos/coost.git", os.path.join("include", "co", "fastring.h")),
}
# needed only by --dna.  native/src/Host.h is what makes the runtime embeddable: a checkout without it is not the right one.
OPTIONAL_DEPS = {
    "dna": ("https://github.com/crustos/DotNetAnywhere.git", os.path.join("native", "src", "Host.h")),
}
DIRNAMES = {"dna": "DotNetAnywhere"}                    # (where a dependency lives beside this repo, when that is not its key)
ALL_DEPS = dict(DEPS, **OPTIONAL_DEPS)


def say(msg):
    print(msg, flush=True)


def step(name):
    say("\n== %s " % name + "=" * max(2, 66 - len(name)))


def die(msg, code=1):
    sys.stderr.write("build.py: %s\n" % msg)
    sys.exit(code)


def run(cmd, cwd=None, env=None, quiet=False):
    if not quiet:
        say("  $ " + " ".join(cmd))
    r = subprocess.run(cmd, cwd=cwd, env=env)
    if r.returncode != 0:
        die("failed (%d): %s" % (r.returncode, " ".join(cmd)), r.returncode)


def capture(cmd, cwd=None):
    r = subprocess.run(cmd, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, universal_newlines=True)
    return r.returncode, r.stdout


# ---- dependencies ---------------------------------------------------------------------------------------------

def dep_path(name):
    return os.environ.get(name.upper() + "_HOME") or os.path.join(SIBLINGS, DIRNAMES.get(name, name))


def dep_ok(name):
    return os.path.exists(os.path.join(dep_path(name), ALL_DEPS[name][1]))


def dep_rev(name):
    rc, out = capture(["git", "-C", dep_path(name), "log", "-1", "--format=%h %s"])
    return out.strip() if rc == 0 else "(not a git checkout)"


def cmd_deps(opts):
    wanted = dict(DEPS, **(OPTIONAL_DEPS if opts["dna"] or opts["wasm"] else {}))
    step("dependencies (%s, beside this repository)" % " and ".join(wanted))
    for name, (url, probe) in wanted.items():
        path = dep_path(name)
        if dep_ok(name):
            if opts["update"] and not opts["offline"] and os.path.isdir(os.path.join(path, ".git")):
                run(["git", "-C", path, "pull", "--ff-only"])
            say("  %-6s %s   %s" % (name, path, dep_rev(name)))
            continue
        if opts["offline"]:
            die("%s is not at %s and --offline forbids cloning it.\n  git clone %s %s" % (name, path, url, path))
        if os.path.exists(path) and os.listdir(path):
            die("%s exists but does not look like %s (no %s)" % (path, name, probe))
        if not shutil.which("git"):
            die("git is needed to clone %s (or put a checkout at %s)" % (name, path))
        run(["git", "clone", url, path])
        if not dep_ok(name):
            die("cloned %s but %s is missing%s" % (url, probe, "\n  (--dna needs the version of DotNetAnywhere that has native/src/Host.h)" if name == "dna" else ""))
        say("  %-6s %s   %s" % (name, path, dep_rev(name)))


def require_deps(opts=None):
    for name in DEPS:
        if not dep_ok(name):
            die("%s not found at %s.  Run `python3 build.py deps` (clones it beside this repo)." % (name, dep_path(name)))
    if opts and (opts.get("dna") or opts.get("wasm")) and not dep_ok("dna"):
        die("--dna and --wasm need DotNetAnywhere at %s (the version with %s).\n  Run `python3 build.py deps --dna`, or set DNA_HOME."
            % (dep_path("dna"), ALL_DEPS["dna"][1]))


def env_for_children():
    env = dict(os.environ)
    env["CRUST_HOME"] = dep_path("crust")
    env["COOST_HOME"] = dep_path("coost")
    env["CRUST"] = dep_path("crust")                      # coost's build.py reads CRUST
    if dep_ok("dna"):
        env["DNA_HOME"] = dep_path("dna")
    if os.path.exists(COMPILER_DLL):
        env["CCS"] = "%s %s" % (dotnet(), COMPILER_DLL)
    return env


# ---- the compiler -----------------------------------------------------------------------------------------------

def dotnet():
    exe = os.environ.get("DOTNET") or shutil.which("dotnet")
    if not exe:
        die("the .NET SDK (dotnet) was not found.  Install .NET SDK 8 or later (its Roslyn compiles the C# compiler).")
    return exe


def sdk_roslyn():
    """(sdk major version, directory holding Microsoft.CodeAnalysis*.dll) of the newest installed SDK."""
    rc, out = capture([dotnet(), "--list-sdks"])
    best = None
    for line in out.splitlines():
        m = re.match(r"(\d+)\.(\d+)\.(\d+)\S*\s+\[(.+)\]", line.strip())
        if not m:
            continue
        ver = tuple(int(x) for x in m.groups()[:3])
        d = os.path.join(m.group(4), "%s" % line.split()[0], "Roslyn", "bincore")
        if os.path.exists(os.path.join(d, "Microsoft.CodeAnalysis.CSharp.dll")) and (best is None or ver > best[0]):
            best = (ver, d)
    if best is None:
        die("no .NET SDK with Roslyn found (`dotnet --list-sdks` printed:\n%s)\n  Install .NET SDK 8 or later." % out.strip())
    if best[0][0] < 8:
        die(".NET SDK %s is too old: 8 or later is needed." % ".".join(map(str, best[0])))
    return best[0][0], best[1]


def compiler_sources():
    return sorted(glob.glob(os.path.join(ROOT, "compiler", "src", "*.cs")))


def compiler_stamp(roslyn):
    h = hashlib.sha256()
    h.update(roslyn.encode())
    for f in compiler_sources():
        h.update(f.encode())
        with open(f, "rb") as fh:
            h.update(fh.read())
    return h.hexdigest()


CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <!-- generated by build.py.  The compiler is built against the Roslyn that ships inside the .NET SDK, so
       nothing is downloaded: compiler/src/CCSharpCompiler.csproj (NuGet) is the normal developer build. -->
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net{major}.0</TargetFramework>
    <LangVersion>10.0</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <UseAppHost>false</UseAppHost>
    <AssemblyName>ccs</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <RestoreSources>{empty}</RestoreSources>
    <NoWarn>CS8632;CS0168;CS0219;CS0162;CS0649;CS0414</NoWarn>
    <OutDir>{out}/</OutDir>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{src}/*.cs" />
    <Reference Include="Microsoft.CodeAnalysis"><HintPath>{roslyn}/Microsoft.CodeAnalysis.dll</HintPath></Reference>
    <Reference Include="Microsoft.CodeAnalysis.CSharp"><HintPath>{roslyn}/Microsoft.CodeAnalysis.CSharp.dll</HintPath></Reference>
  </ItemGroup>
</Project>
"""


def cmd_compiler(opts):
    step("compiler (Roslyn from the .NET SDK, offline)")
    major, roslyn = sdk_roslyn()
    stamp_file = os.path.join(COMPILER_DIR, "ccs.stamp")
    stamp = compiler_stamp(roslyn)
    if os.path.exists(COMPILER_DLL) and os.path.exists(stamp_file) and open(stamp_file).read() == stamp:
        say("  (current) " + os.path.relpath(COMPILER_DLL, ROOT))
        return
    proj = os.path.join(COMPILER_DIR, "proj")
    empty = os.path.join(BUILD, "empty-nuget-source")
    os.makedirs(proj, exist_ok=True)
    os.makedirs(empty, exist_ok=True)
    with open(os.path.join(proj, "ccs.csproj"), "w") as f:
        f.write(CSPROJ.format(major=major, empty=empty, out=COMPILER_DIR, src=os.path.join(ROOT, "compiler", "src"), roslyn=roslyn))
    run([dotnet(), "build", "-c", "Release", "-nologo", "-v", "q", os.path.join(proj, "ccs.csproj")])
    if not os.path.exists(COMPILER_DLL):
        die("the build reported success but %s is missing" % COMPILER_DLL)
    with open(stamp_file, "w") as f:
        f.write(stamp)
    say("  built " + os.path.relpath(COMPILER_DLL, ROOT))


def require_compiler():
    if not os.path.exists(COMPILER_DLL):
        die("the compiler is not built.  Run `python3 build.py compiler`.")


# ---- coost -------------------------------------------------------------------------------------------------------

def cmd_coost(opts):
    step("coost (built through crust's cpprust, as coost's own build.py does)")
    require_deps()
    env = env_for_children()
    run([sys.executable, "build.py"], cwd=dep_path("coost"), env=env)
    if opts["coost_test"]:
        run([sys.executable, "build.py", "test"], cwd=dep_path("coost"), env=env)


# ---- the corelib --------------------------------------------------------------------------------------------------

def cmd_corelib(opts):
    step("corelib (corelib/src compiles; corelib/native lowers and compiles)")
    require_deps()
    require_compiler()
    env = env_for_children()
    tmp = os.path.join(BUILD, "corelib-check")
    shutil.rmtree(tmp, ignore_errors=True)
    os.makedirs(os.path.join(tmp, "src"))
    with open(os.path.join(tmp, "src", "Check.cs"), "w") as f:
        f.write("class Check { static int Main() { return 0; } }\n")
    # 1. the C# corelib is the only thing a program can name: it must compile on its own
    rc, out = capture([dotnet(), COMPILER_DLL, os.path.join(tmp, "src"), "Check", "--home=" + ROOT], cwd=tmp)
    if rc != 0 or "generated" not in out:
        die("the corelib does not compile:\n" + out.strip())
    say("  corelib/src: %d files compile" % len(glob.glob(os.path.join(ROOT, "corelib", "src", "**", "*.cs"), recursive=True)))
    # 2. the native helpers: every header lowers through cpprust and compiles as C
    sys.path.insert(0, os.path.join(ROOT, "crust"))
    os.environ.update({"CRUST_HOME": dep_path("crust"), "COOST_HOME": dep_path("coost")})
    import unit                                          # noqa: E402
    heads = sorted(os.path.basename(h) for h in glob.glob(os.path.join(ROOT, "corelib", "native", "include", "cs", "*.h")))
    prog = os.path.join(tmp, "native_check.cc")
    with open(prog, "w") as f:
        f.write("".join('#include "cs/%s"\n' % h for h in heads) + "int main() { return 0; }\n")
    unit_cc, _ = unit.assemble(prog, tmp, "native_unit")
    try:
        unit.lower(unit_cc, os.path.join(tmp, "native_unit.c"))
    except RuntimeError as e:
        die("corelib/native does not lower:\n" + str(e))
    cc = os.environ.get("CC") or shutil.which("cc") or shutil.which("gcc")
    run([cc, "-w", "-o", os.path.join(tmp, "native_check"), os.path.join(tmp, "native_unit.c"), "-lm"], quiet=True)
    say("  corelib/native: %s lower and compile" % ", ".join(heads))


# ---- tests and examples -------------------------------------------------------------------------------------------

def cmd_test(opts):
    step("tests (each case vs real .NET, compiled with gcc%s)" % (" and shivyc" if opts["shivyc"] else ""))
    require_deps()
    require_compiler()
    env = env_for_children()
    if opts["shivyc"]:
        env["SHIVYC"] = "1"
    if opts["cc"]:
        env["CC"] = opts["cc"]
    if opts["wasm"]:
        env["CCS_WASM"] = "1"
    if not shutil.which("dotnet"):
        say("  note: no dotnet, so there is no .NET reference to compare with")
    if not opts["names"]:
        run([sys.executable, os.path.join(ROOT, "crust", "test_inputs.py")], env=env)          # what counts as a program
        run([sys.executable, os.path.join(ROOT, "crust", "test_partition.py")], env=env)       # --dna: native / managed classes
        run([sys.executable, os.path.join(ROOT, "crust", "test_foreign.py")], env=env)         # extern members with a [Cpp] template
        run([sys.executable, os.path.join(ROOT, "crust", "test_arena.py")], env=env)           # arena classes, foreign headers and types
    run([sys.executable, os.path.join(ROOT, "crust", "run_tests.py")] + opts["names"], env=env)


def ccs2c_cmd(opts, *extra):
    """crust/ccs2c.py on the user's inputs with the options that apply to every use."""
    if not opts["names"]:
        die("no input: give a .cs file, a folder, a .csproj or a .sln")
    cmd = [sys.executable, os.path.join(ROOT, "crust", "ccs2c.py")] + opts["names"]
    if opts["main"]:
        cmd.append("--main=" + opts["main"])
    if opts["name"]:
        cmd.append("--name=" + opts["name"])
    if opts["cc"]:
        cmd.append("--cc=" + opts["cc"])
    if opts["debug"]:
        cmd.append("--debug")
    if opts["shivyc"]:
        cmd.append("--shivyc")
    if opts["dna"]:
        cmd.append("--dna")
    if opts["wasm"]:
        cmd.append("--wasm")
    return cmd + list(extra)


def program_name(opts):
    sys.path.insert(0, os.path.join(ROOT, "crust"))
    import inputs
    return opts["name"] or inputs.project_name(opts["names"])


def cmd_examples(opts):
    step("examples")
    require_deps()
    require_compiler()
    env = env_for_children()
    os.makedirs(os.path.join(BUILD, "examples"), exist_ok=True)
    for ex in sorted(glob.glob(os.path.join(ROOT, "examples", "*"))):
        name = os.path.basename(ex)
        exe = os.path.join(BUILD, "examples", name)
        cmd = [sys.executable, os.path.join(ROOT, "crust", "ccs2c.py"), ex, "--name=" + name, "--exe=" + exe]
        if opts["cc"]:
            cmd.append("--cc=" + opts["cc"])
        run(cmd, env=env, quiet=True)
        r = subprocess.run([exe], stdout=subprocess.PIPE, universal_newlines=True)
        say("  %-10s rc=%d  %s" % (name, r.returncode, " | ".join(r.stdout.strip().splitlines())[:90]))


def cmd_run(opts):
    require_deps(opts)
    require_compiler()
    r = subprocess.run(ccs2c_cmd(opts, "--run", "--", *opts["rest"]), env=env_for_children())
    sys.exit(r.returncode)


def tool(cmd):
    """Run crust/ccs2c.py.  Its output IS the result (diagnostics, `built X`), so a failure just passes its status on."""
    r = subprocess.run(cmd, env=env_for_children())
    if r.returncode != 0:
        sys.exit(r.returncode)


def cmd_compile(opts):
    require_deps(opts)
    require_compiler()
    exe = os.path.abspath(opts["out"]) if opts["out"] else os.path.join(BUILD, "bin", program_name(opts))
    tool(ccs2c_cmd(opts, "--exe=" + exe))


def cmd_convert(opts):
    require_deps(opts)
    require_compiler()
    dest = os.path.abspath(opts["out"]) if opts["out"] else os.path.join(BUILD, "convert", program_name(opts))
    tool(ccs2c_cmd(opts, "--convert=" + dest, *(["--c"] if opts["c"] else [])))


# ---- status / clean -----------------------------------------------------------------------------------------------

def cmd_status(opts):
    step("status")
    say("  repo      %s" % ROOT)
    for name in DEPS:
        say("  %-9s %s  %s" % (name, dep_path(name) if dep_ok(name) else dep_path(name) + "  (MISSING: python3 build.py deps)",
                              dep_rev(name) if dep_ok(name) else ""))
    say("  %-9s %s  %s" % ("dna", dep_path("dna") if dep_ok("dna") else dep_path("dna") + "  (optional, for --dna: python3 build.py deps --dna)",
                          dep_rev("dna") if dep_ok("dna") else ""))
    say("  mcs       %s   (--dna: compiles DotNetAnywhere's corlib)" % (shutil.which("mcs") or "(not found)"))
    sys.path.insert(0, os.path.join(ROOT, "crust"))
    import ccs2c
    ok, why = ccs2c.wasm_available()
    say("  wasm      %s" % ("clang + lld + wasi-libc (%s) + llvm-ar + node: --wasm can work" % ccs2c.wasi_sysroot() if ok else "--wasm cannot: " + why))
    d = os.environ.get("DOTNET") or shutil.which("dotnet")
    say("  dotnet    %s" % (d or "(not found)"))
    if d:
        try:
            major, roslyn = sdk_roslyn()
            say("  roslyn    %s  (SDK %d)" % (roslyn, major))
        except SystemExit:
            pass
    say("  compiler  %s" % (COMPILER_DLL if os.path.exists(COMPILER_DLL) else "(not built: python3 build.py compiler)"))
    say("  cc        %s" % (os.environ.get("CC") or shutil.which("cc") or shutil.which("gcc") or "(not found)"))
    say("  git       %s" % (shutil.which("git") or "(not found)"))


def cmd_clean(opts):
    step("clean")
    shutil.rmtree(BUILD, ignore_errors=True)
    shutil.rmtree(os.path.join(ROOT, "crust", "out"), ignore_errors=True)
    if dep_ok("coost") and dep_ok("crust"):
        subprocess.run([sys.executable, "build.py", "clean"], cwd=dep_path("coost"), env=env_for_children())
    say("  removed build/ (the dependencies themselves are left alone)")


def cmd_all(opts):
    cmd_deps(opts)
    cmd_compiler(opts)
    cmd_coost(opts)
    cmd_corelib(opts)
    if not opts["no_test"]:
        cmd_test(opts)
    cmd_examples(opts)
    step("done")
    say("  python3 build.py run examples/example1/src")


COMMANDS = {
    "all": cmd_all, "deps": cmd_deps, "compiler": cmd_compiler, "coost": cmd_coost, "corelib": cmd_corelib,
    "test": cmd_test, "examples": cmd_examples, "run": cmd_run, "compile": cmd_compile, "convert": cmd_convert,
    "status": cmd_status, "clean": cmd_clean,
}


def main(argv):
    opts = {"offline": False, "update": False, "shivyc": False, "dna": False, "wasm": False, "no_test": False, "coost_test": False, "c": False,
            "debug": False, "out": None, "main": None, "name": None, "cc": None, "names": [], "rest": []}
    value_opts = {"-o": "out", "--main": "main", "--name": "name", "--cc": "cc"}
    flag_opts = {"--offline": "offline", "--update": "update", "--shivyc": "shivyc", "--dna": "dna", "--wasm": "wasm", "--no-test": "no_test",
                 "--test": "coost_test", "--c": "c", "--debug": "debug"}
    cmd = None
    i = 0
    while i < len(argv):
        a = argv[i]
        if a in ("-h", "--help", "help"):
            print(__doc__)
            return 0
        if a == "--":
            opts["rest"] = argv[i + 1:]
            break
        if a in flag_opts:
            opts[flag_opts[a]] = True
        elif a in value_opts:
            i += 1
            if i >= len(argv):
                die("%s needs a value (see --help)" % a)
            opts[value_opts[a]] = argv[i]
        elif "=" in a and a.split("=", 1)[0] in value_opts:
            opts[value_opts[a.split("=", 1)[0]]] = a.split("=", 1)[1]
        elif cmd is None and a in COMMANDS:
            cmd = a
        elif a.startswith("-") and len(a) > 1:
            die("unknown option %s (see --help)" % a)
        else:
            opts["names"].append(a)
        i += 1
    if cmd in ("compile", "convert", "run") or cmd is None:
        pass
    COMMANDS[cmd or "all"](opts)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
