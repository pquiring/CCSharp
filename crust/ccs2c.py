#!/usr/bin/env python3
"""ccs2c -- CC# (Roslyn) C# -> Crust C++ subset -> C, and optionally build + run.

    python3 crust/ccs2c.py INPUT... [options]

INPUT is any mix of C# files, folders, .csproj and .sln (see crust/inputs.py); together they are ONE program.

    --main=Class      the class whose static Main is the entry point (needed when there are several)
    --name=Name       the program's name (default: the first input's name)
    --convert=DIR     write the generated C++ into DIR            (add --c for the lowered, self-contained C too)
    -o FILE.c         write the lowered C to FILE.c
    --exe=PATH        build a native executable               (gcc by default; --shivyc uses Crust's own compiler)
    --run             build and run it; arguments after `--` go to the program
    --cc=CC           the C compiler (default: $CC, else cc / gcc)
    --debug           -g -O0 instead of -O2
    --shivyc          compile with Crust's own compiler (shivyc) instead of gcc
    --bindings=DIR    a folder of C# that only names foreign C (the C flavor of a library's bindings: types and externs with [Crust.Cpp] templates, no
                      code); its .cs files are part of the program.  May be repeated.
    --unity-types     the Unity value types are built in: Vector2, Vector3, Quaternion, Color and the enums ForceMode2D, RigidbodyType2D, Space, in namespace
                      UnityEngine (unity/*.cs, added to the program as if listed).  Their operators (`a + b`, `-a`, `a * 2f`, `a == b`, `a += b`) are
                      calls of the struct's operator methods, as for any struct.  A game says `using UnityEngine;` and does not carry a copy.
    --unity-engine    a small UnityEngine, in C#: GameObject, Transform, MonoBehaviour, SpriteRenderer, Camera, Time and the loop that runs them
                      (unity-engine/*.cs), on top of the Unity value types (so it implies --unity-types).  Its objects are arena classes (see the README), and a
                      script is a class that derives from MonoBehaviour.
    --include=DIR     the folder the C compiler finds the foreign headers in (`-I DIR` when the C is built).  The translation does not read them: each is
                      left as an `#include` at the top of the C file, which is where its types have to be declared.  Accepted so that a build can state it
                      once for both steps.  May be repeated.
    --dna             a class Crust cannot lower (or marked [Managed]) stays C# and runs on DotNetAnywhere, called from the native
                      code and calling it; needs DotNetAnywhere (the crustos fork) beside this repo, or DNA_HOME, and mono-mcs
    --wasm            build for WebAssembly (wasm32-wasi) instead of for this machine: NAME.wasm, a launcher NAME that runs it under node, and
                      beside them run_wasm.mjs (the host: it also hosts DotNetAnywhere's compiler from CIL to wasm), NAME.managed.dll and corlib.dll.
                      Implies --dna: what Crust cannot lower runs on DotNetAnywhere inside the same module.  Needs clang, lld, wasi-libc, llvm-ar and
                      node (see `python3 build.py status`).  --exe=PATH names the launcher; the module is PATH.wasm (or PATH, if it ends in .wasm)

Pipeline:
    CCSharpCompiler --srclist=...       Roslyn: the C# files -> cpp/*.cpp
    unit.py + tools/cpprust.py          the coost sources they reach are spliced in, then lowered to C
    cc                                  the executable (only with --exe / --run)

Environment (all default to what `python3 build.py` sets up):
    WASI_SYSROOT  where wasi-libc is (headers in DIR/include/wasm32-wasi, libraries in DIR/lib/wasm32-wasi), for --wasm   (default /usr)
    DNA_HOME     a checkout of https://github.com/crustos/DotNetAnywhere   (default: ../DotNetAnywhere), for --dna
    CRUST_HOME   a checkout of https://github.com/brentharts/crust   (default: ../crust)
    COOST_HOME   a checkout of https://github.com/crustos/coost      (default: ../coost)
    CCS          command that runs the compiler, e.g. "dotnet build/compiler/ccs.dll"
    CC           the C compiler
"""
import os
import re
import shlex
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import inputs as inputs_mod                              # noqa: E402
import unit                                              # noqa: E402


def crust_home():
    unit.require()
    return unit.crust_home()


def compiler_cmd():
    """CCS, else the compiler build.py makes (build/compiler/ccs.dll), else the offline-build output."""
    c = os.environ.get("CCS")
    if c:
        return shlex.split(c)
    for cand in (os.path.join(REPO, "build", "compiler", "ccs.dll"), os.path.join(HERE, "out", "ccs.dll")):
        if os.path.exists(cand):
            return ["dotnet", cand]
    sys.exit("ccs2c: no compiler: run `python3 build.py compiler` (or set CCS)")


def c_compiler(cc=None):
    c = cc or os.environ.get("CC") or shutil.which("cc") or shutil.which("gcc")
    if not c:
        sys.exit("ccs2c: no C compiler found (install gcc, or pass --cc / set CC)")
    return c


class Refused(Exception):
    pass


def wasi_sysroot():
    return os.environ.get("WASI_SYSROOT") or "/usr"


def wasm_available():
    """(True, "") when --wasm can work here, else (False, why): clang that targets wasm32 with lld, wasi-libc, llvm-ar (DotNetAnywhere's archive), node."""
    for tool, hint in (("clang", "apt install clang"), ("wasm-ld", "apt install lld"), ("llvm-ar", "apt install llvm"),
                       ("node", "node 20 or later runs the module")):
        if not shutil.which(tool):
            return False, "%s is not installed (%s)" % (tool, hint)
    if not os.path.isdir(os.path.join(wasi_sysroot(), "include", "wasm32-wasi")):
        return False, "no wasi-libc under %s (apt install wasi-libc libclang-rt-dev-wasm32, or set WASI_SYSROOT)" % wasi_sysroot()
    return True, ""


def wasm_compiler(cc=None):
    """clang, whatever $CC says about this machine (gcc cannot target wasm); --cc names another one that can."""
    if cc:
        return cc
    env = os.environ.get("CC")
    return env if env and "clang" in os.path.basename(env) else "clang"


def _wasm_cflags():
    root = wasi_sysroot()
    # -nostdlibinc: without it clang also searches this machine's /usr/include, which is the wrong libc (no wasm32 ABI)
    return ["--target=wasm32-wasi", "--sysroot=" + root, "-nostdlibinc", "-isystem", os.path.join(root, "include", "wasm32-wasi")]


# the stack is 8 MB, as a native program's is (wasm's default 64 KB is too small for C recursion); the table is exported and growable because the
# host puts the functions that DotNetAnywhere compiles from CIL at run time into it (native/src/WasmJIT.c)
_WASM_LDFLAGS = ["-fuse-ld=lld", "-Wl,-z,stack-size=8388608", "-Wl,--export-table", "-Wl,--growable-table"]


def wasm_paths(exe):
    """(launcher, module) for what --exe names: PATH gives PATH and PATH.wasm; PATH.wasm gives PATH and PATH.wasm."""
    return (exe[:-5], exe) if exe.endswith(".wasm") else (exe, exe + ".wasm")


def _wasm_package(exe, name, home, has_managed):
    """Beside the module: the host script, and a launcher that runs it from wherever the program is started."""
    launcher, module = wasm_paths(exe)
    d = os.path.dirname(os.path.abspath(launcher))
    host = os.path.join(home, "tools", "run_wasm.mjs")
    if not os.path.exists(host):
        raise Refused("DotNetAnywhere at %s has no tools/run_wasm.mjs (it is the host that runs a wasm build; use the version that has --wasm)" % home)
    shutil.copy(host, os.path.join(d, "run_wasm.mjs"))
    script = ('#!/bin/sh\n# CC# --wasm: runs %s under node.  Needs node 20 or later.\n'
              'd=$(cd "$(dirname "$0")" && pwd)\n' % os.path.basename(module))
    if has_managed:
        script += '[ -z "$CCS_MANAGED_DLL" ] && [ -f "$d/%s.managed.dll" ] && export CCS_MANAGED_DLL="$d/%s.managed.dll"\n' % (name, name)
    script += 'exec node --no-warnings "$d/run_wasm.mjs" --app "$d/%s" "$@"\n' % os.path.basename(module)
    with open(launcher, "w") as f:
        f.write(script)
    os.chmod(launcher, 0o755)


class ShivycUnavailable(Exception):
    """shivyc cannot compile this program for a reason that is shivyc's (a glibc header it does not bundle)."""


def dna_home():
    """The DotNetAnywhere checkout (the crustos fork: native/src/Host.h is what makes it embeddable)."""
    h = os.environ.get("DNA_HOME") or os.path.join(os.path.dirname(REPO), "DotNetAnywhere")
    if not os.path.exists(os.path.join(h, "native", "src", "Host.h")):
        raise Refused("--dna needs DotNetAnywhere (https://github.com/crustos/DotNetAnywhere, the version with native/src/Host.h) at %s:\n"
                      "  clone it there, or set DNA_HOME" % h)
    return h


def dna_available():
    """(True, "") when --dna can work here, else (False, why): DotNetAnywhere beside this repo, and mono-mcs to build its corlib."""
    try:
        dna_home()
    except Refused as e:
        return False, str(e).splitlines()[0]
    if not shutil.which("mcs"):
        return False, "mcs is not installed (apt install mono-mcs), and DotNetAnywhere's corlib is built with it"
    return True, ""


_dna_ready = {}


def dna_run(args, home, bdir):
    p = subprocess.run([sys.executable, os.path.join(home, "build.py"), "--build-dir", bdir] + args,
                       stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if p.returncode != 0:
        raise Refused("building DotNetAnywhere failed (build.py %s):\n%s" % (" ".join(args), p.stdout.decode("utf-8", "replace").strip()[-800:]))


def dna_prepare(wasm=False):
    """DotNetAnywhere built into build/dna of this repo (its own objects, so the checkout's own build is left alone): the runtime library
    libdna.a, and corlib.dll, which the managed classes are compiled against and which must sit beside them.  With wasm: the same for wasm32, in
    build/dna-wasm (libdna_wasm.a).  Returns (home, build dir)."""
    home = dna_home()
    bdir = os.path.join(REPO, "build", "dna-wasm" if wasm else "dna")
    if (home, wasm) not in _dna_ready:
        dna_run((["--wasm"] if wasm else []) + ["--lib-only"], home, bdir)
        if not os.path.exists(os.path.join(bdir, "corlib.dll")):
            raise Refused("DotNetAnywhere's corlib.dll was not built (it needs mcs: apt install mono-mcs)")
        _dna_ready[(home, wasm)] = True
    return home, bdir


def dna_link(c_text, cpp_dir, name, exe, cc=None, debug=False, wasm=False):
    """The hybrid executable: the lowered C, the glue that calls DotNetAnywhere, and the runtime (with the native functions that managed code
    may [DllImport] in its FFI table).  The managed assembly and corlib.dll go beside it.  With wasm: a WebAssembly module and its launcher
    (see wasm_paths); the managed assembly and corlib.dll go beside them."""
    home, bdir = dna_prepare(wasm)
    glue = os.path.join(cpp_dir, name + ".bridge.c")
    manifest = os.path.join(cpp_dir, name + ".ffi.json")
    if not os.path.exists(glue):                                  # nothing in this program is managed
        return build_exe(c_text, exe, cc=cc, debug=debug, wasm=wasm)
    if os.path.exists(manifest):
        dna_run((["--wasm"] if wasm else []) + ["--ffi", manifest, "--lib-only", "--no-corlib"], home, bdir)
        lib = os.path.join(bdir, "libdna_ffi_wasm.a" if wasm else "libdna_ffi.a")
    else:
        lib = os.path.join(bdir, "libdna_wasm.a" if wasm else "libdna.a")
    out = wasm_paths(exe)[1] if wasm else exe
    d = os.path.dirname(os.path.abspath(exe))
    os.makedirs(d, exist_ok=True)
    cfile = out + ".c"
    with open(cfile, "w") as f:
        f.write(c_text)
    if wasm:
        cmd = [wasm_compiler(cc)] + _cflags(debug) + _wasm_cflags() + ["-I", os.path.join(home, "native", "src"), "-o", out, cfile, glue, lib, "-lm"] + _WASM_LDFLAGS
    else:
        cmd = [c_compiler(cc)] + _cflags(debug) + ["-I", os.path.join(home, "native", "src"), "-o", exe, cfile, glue, lib, "-lm", "-lpthread"]
    p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if p.returncode != 0:
        raise Refused("C compiler failed:\n" + re.sub(r"\x1b\[[0-9;]*m", "", p.stdout.decode("utf-8", "replace")).strip()[-800:])
    dll = os.path.join(cpp_dir, name + ".managed.dll")
    if os.path.exists(dll):
        shutil.copy(dll, d)
    shutil.copy(os.path.join(bdir, "corlib.dll"), d)
    if wasm:
        _wasm_package(exe, name, home, os.path.exists(dll))


def build_run_dna(c_text, cpp_dir, name="Program", args=(), timeout=60, cc=None):
    """Link the hybrid executable and run it: (exit status, stdout)."""
    tmp = tempfile.mkdtemp(prefix="ccs2c-dna-")
    elsewhere = tempfile.mkdtemp(prefix="ccs2c-cwd-")
    try:
        exe = os.path.join(tmp, name)
        dna_link(c_text, cpp_dir, name, exe, cc=cc)
        # started from another directory, as a program is: it must find its managed assembly and corlib.dll beside itself
        # (and with the leak check on: a managed object that native code never let go of fails the case, whatever it printed)
        r = subprocess.run([exe] + list(args), cwd=elsewhere, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout,
                           env=dict(os.environ, CCS_CHECK_HANDLES="1"))
        return r.returncode, r.stdout.decode("utf-8", "replace")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
        shutil.rmtree(elsewhere, ignore_errors=True)


def build_run_wasm(c_text, cpp_dir, name="Program", args=(), timeout=120, cc=None):
    """Build the program for wasm32-wasi and run it under node: (exit status, stdout).  Like build_run_dna, it is started from another directory and
    with the leak check on."""
    tmp = tempfile.mkdtemp(prefix="ccs2c-wasm-")
    elsewhere = tempfile.mkdtemp(prefix="ccs2c-cwd-")
    try:
        exe = os.path.join(tmp, name)
        dna_link(c_text, cpp_dir, name, exe, cc=cc, wasm=True)
        r = subprocess.run([exe] + list(args), cwd=elsewhere, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout,
                           env=dict(os.environ, CCS_CHECK_HANDLES="1"))
        return r.returncode, r.stdout.decode("utf-8", "replace")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
        shutil.rmtree(elsewhere, ignore_errors=True)


UNITY_DIR = os.path.join(REPO, "unity")
UNITY_ENGINE_DIR = os.path.join(REPO, "unity-engine")


def to_cpp(inputs, main=None, workdir=None, name=None, dna=False, bindings=(), wasm=False, unity_types=False, unity_engine=False, describe=None):
    """C# inputs (files / folders / .csproj / .sln, one program) in; returns (cpp_text, cpp_dir).
    Raises Refused with the diagnostics."""
    if isinstance(inputs, str):
        inputs = [inputs]
    try:
        files = inputs_mod.expand(inputs)
        for b in bindings:                                      # code-free C# that names foreign C: part of the program
            files += [f for f in inputs_mod.expand([b]) if f not in files]
        if unity_types or unity_engine:                                         # the built-in UnityEngine value types (unity/*.cs)
            files += [f for f in inputs_mod.expand([UNITY_DIR]) if f not in files]
        if unity_engine:                                        # the engine (unity-engine/*.cs): GameObject, Transform, MonoBehaviour ...
            files += [f for f in inputs_mod.expand([UNITY_ENGINE_DIR]) if f not in files]
    except inputs_mod.InputError as e:
        raise Refused(str(e))
    name = name or "Program"
    work = workdir or tempfile.mkdtemp(prefix="ccs2c-")
    listing = os.path.join(work, "sources.txt")
    with open(listing, "w") as f:
        f.write("\n".join(files) + "\n")
    cmd = compiler_cmd() + [files[0], name, "--crust", "--home=" + REPO, "--srclist=" + listing]
    if main:
        cmd.append("--main=" + main)
    if describe:
        cmd.append("--describe=" + os.path.abspath(describe))
    if dna:
        cmd += ["--dna", "--dna-corlib=" + os.path.join(dna_prepare(wasm)[1], "corlib.dll")]
    p = subprocess.run(cmd, cwd=work, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    out = p.stdout.decode("utf-8", "replace")
    if p.returncode != 0:
        raise Refused("\n".join(l for l in out.strip().splitlines() if not l.startswith("build:")))
    cpp_dir = os.path.join(work, "cpp")
    if describe:
        return "", cpp_dir
    with open(os.path.join(cpp_dir, name + ".main.cpp")) as f:
        return f.read(), cpp_dir


def to_c(cpp_text, cpp_dir, name="Program"):
    """The program's translation unit (coost sources spliced in), lowered to C by cpprust."""
    prog = os.path.join(cpp_dir, name + ".main.cpp")
    unit_cc, _ = unit.assemble(prog, cpp_dir, "unit")
    try:
        return unit.lower(unit_cc, os.path.join(cpp_dir, "unit.c"))
    except RuntimeError as e:
        raise Refused("cpprust: " + str(e))


def _cflags(debug):
    return ["-g", "-O0", "-w"] if debug else ["-O2", "-w"]


def build_run(c_text, args=(), timeout=30, cc=None):
    """Compile lowered C with the C compiler (gcc) and run it: (exit status, stdout)."""
    tmp = tempfile.mkdtemp(prefix="ccs2c-run-")
    try:
        cfile = os.path.join(tmp, "p.c")
        with open(cfile, "w") as f:
            f.write(c_text)
        exe = os.path.join(tmp, "p")
        p = subprocess.run([c_compiler(cc), "-w", "-o", exe, cfile, "-lm"], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if p.returncode != 0:
            raise Refused("generated C did not compile:\n" + p.stdout.decode("utf-8", "replace"))
        r = subprocess.run([exe] + list(args), stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout)
        return r.returncode, r.stdout.decode("utf-8", "replace")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def build_run_shivyc(c_text, args=(), timeout=60):
    """Compile with Crust's own compiler (shivyc) and run.  Experimental: see README."""
    home = crust_home()
    tmp = tempfile.mkdtemp(prefix="ccs2c-shivyc-")
    try:
        cfile = os.path.join(tmp, "p.c")
        with open(cfile, "w") as f:
            f.write(c_text)
        exe = os.path.join(tmp, "p")
        p = subprocess.run([sys.executable, os.path.join(home, "crust"), cfile, "-o", exe],
                           stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=300)
        if p.returncode != 0:
            msg = re.sub(r"\x1b\[[0-9;]*m", "", p.stdout.decode("utf-8", "replace"))
            #the only excuse accepted: a system header shivyc does not bundle (coost's fs / time need <errno.h>)
            m = re.search(r"error: unable to read included file\s*\n\s*#include <([\w./]+)>", msg)
            if m:
                raise ShivycUnavailable("<%s> is not bundled with shivyc" % m.group(1))
            raise Refused("shivyc rejected the generated C:\n" + msg.strip()[-600:])
        r = subprocess.run([exe] + list(args), stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout)
        return r.returncode, r.stdout.decode("utf-8", "replace")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def build_exe(c_text, exe, shivyc=False, cc=None, debug=False, wasm=False):
    """Compile lowered C to a native executable: gcc (or $CC / --cc), or Crust's own compiler (shivyc).  With wasm: a WebAssembly module with its
    launcher (see wasm_paths), clang with wasi-libc."""
    d = os.path.dirname(os.path.abspath(exe))
    os.makedirs(d, exist_ok=True)
    if wasm:
        launcher, module = wasm_paths(exe)
        cfile = module + ".c"
        with open(cfile, "w") as f:
            f.write(c_text)
        cmd = [wasm_compiler(cc)] + _cflags(debug) + _wasm_cflags() + ["-o", module, cfile, "-lm"] + _WASM_LDFLAGS
        p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if p.returncode != 0:
            raise Refused("C compiler failed (wasm32-wasi):\n" + re.sub(r"\x1b\[[0-9;]*m", "", p.stdout.decode("utf-8", "replace")).strip()[-800:])
        _wasm_package(exe, os.path.basename(launcher), dna_home(), False)
        return
    cfile = exe + ".c"
    with open(cfile, "w") as f:
        f.write(c_text)
    if shivyc:
        cmd = [sys.executable, os.path.join(crust_home(), "crust"), cfile, "-o", exe]
    else:
        cmd = [c_compiler(cc)] + _cflags(debug) + ["-o", exe, cfile, "-lm"]
    p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if p.returncode != 0:
        raise Refused("C compiler failed:\n" + re.sub(r"\x1b\[[0-9;]*m", "", p.stdout.decode("utf-8", "replace")).strip()[-800:])


def convert(cpp_dir, dest, name, c_text=None):
    """Copy the generated C++ (and, if given, the lowered C) into dest.  Returns the files written."""
    os.makedirs(dest, exist_ok=True)
    written = []
    for f in sorted(os.listdir(cpp_dir)):
        if f.endswith(".cpp") and f != "unit.cc":
            shutil.copy(os.path.join(cpp_dir, f), os.path.join(dest, f))
            written.append(os.path.join(dest, f))
    # --dna: what the native/managed bridge made beside the C++: the glue, the FFI manifest, the managed assembly and its sources, the partition
    for f in sorted(os.listdir(cpp_dir)):
        if f.endswith((".bridge.c", ".ffi.json", ".managed.dll", ".partition.json")):
            shutil.copy(os.path.join(cpp_dir, f), os.path.join(dest, f))
            written.append(os.path.join(dest, f))
    managed = os.path.join(cpp_dir, "managed")
    if os.path.isdir(managed):
        shutil.rmtree(os.path.join(dest, "managed"), ignore_errors=True)
        shutil.copytree(managed, os.path.join(dest, "managed"))
        written.append(os.path.join(dest, "managed"))
    if c_text is not None:
        p = os.path.join(dest, name + ".c")
        with open(p, "w") as f:
            f.write(c_text)
        written.append(p)
    return written


def parse_args(argv):
    o = {"inputs": [], "main": None, "name": None, "convert": None, "c": False, "out": None, "exe": None,
         "run": False, "cc": None, "debug": False, "shivyc": False, "dna": False, "wasm": False, "unity_types": False, "unity_engine": False, "bindings": [], "include": [], "args": []}
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--":
            o["args"] = argv[i + 1:]
            break
        if a.startswith("--main="):
            o["main"] = a[7:]
        elif a.startswith("--name="):
            o["name"] = a[7:]
        elif a.startswith("--convert="):
            o["convert"] = a[10:]
        elif a.startswith("--exe="):
            o["exe"] = a[6:]
        elif a.startswith("--cc="):
            o["cc"] = a[5:]
        elif a == "--exe":
            i += 1
            o["exe"] = argv[i]
        elif a == "-o":
            i += 1
            o["out"] = argv[i]
        elif a == "--c":
            o["c"] = True
        elif a == "--run":
            o["run"] = True
        elif a == "--debug":
            o["debug"] = True
        elif a == "--shivyc":
            o["shivyc"] = True
        elif a == "--dna":
            o["dna"] = True
        elif a == "--wasm":
            o["wasm"] = True
        elif a == "--unity-types":
            o["unity_types"] = True
        elif a == "--unity-engine":
            o["unity_engine"] = True
        elif a.startswith("--bindings="):
            o["bindings"].append(a[11:])
        elif a.startswith("--include="):
            o["include"].append(a[10:])
        elif a.startswith("-"):
            sys.exit("ccs2c: unknown option %s" % a)
        else:
            o["inputs"].append(a)
        i += 1
    return o


def main(argv):
    if not argv or argv[0] in ("-h", "--help"):
        print(__doc__)
        return 2
    o = parse_args(argv)
    if not o["inputs"]:
        print("ccs2c: no input (a .cs file, a folder, a .csproj or a .sln)")
        return 2
    if o["wasm"]:
        if o["shivyc"]:
            print("ccs2c: --shivyc cannot target wasm (it makes x86 code); --wasm uses clang")
            return 2
        ok, why = wasm_available()
        if not ok:
            print("ccs2c: --wasm: " + why)
            return 1
        o["dna"] = True                                  # (what Crust cannot lower runs on DotNetAnywhere inside the same module)
    name = o["name"] or inputs_mod.project_name(o["inputs"])
    need_c = o["c"] or o["out"] or o["exe"] or o["run"] or not o["convert"]
    try:
        cpp, cpp_dir = to_cpp(o["inputs"], o["main"], name=name, dna=o["dna"], bindings=o["bindings"], wasm=o["wasm"], unity_types=o["unity_types"], unity_engine=o["unity_engine"])
        c = to_c(cpp, cpp_dir, name) if need_c else None
        if o["convert"]:
            for f in convert(cpp_dir, o["convert"], name, c if o["c"] else None):
                print("wrote " + f)
        def build(exe):
            if o["dna"]:
                dna_link(c, cpp_dir, name, exe, cc=o["cc"], debug=o["debug"], wasm=o["wasm"])
            else:
                build_exe(c, exe, shivyc=o["shivyc"], cc=o["cc"], debug=o["debug"])
        if o["exe"]:
            build(o["exe"])
            print("built " + o["exe"])
        if o["out"]:
            with open(o["out"], "w") as f:
                f.write(c)
            print("wrote " + o["out"])
        if o["run"]:
            tmp = tempfile.mkdtemp(prefix="ccs2c-exe-")
            exe = os.path.join(tmp, name)
            build(exe)
            r = subprocess.run([exe] + o["args"], cwd=tmp)
            shutil.rmtree(tmp, ignore_errors=True)
            return r.returncode
        if not (o["convert"] or o["exe"] or o["out"]):
            sys.stdout.write(c)
    except Refused as e:
        print(e)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
