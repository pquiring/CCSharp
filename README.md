# C# -> C++ -> C = CC#

CC# is a C# cross compiler written in C# on top of Roslyn. It turns a C# program, or a whole C# project, into the
C++ subset that [Crust](https://github.com/brentharts/crust) accepts. Crust's `cpprust` lowers that to plain C, and
a C compiler (gcc by default) makes the executable.

```
C# --Roslyn--> Crust C++ subset --cpprust--> C --gcc--> native
 (CC#, this repo)                (crust)             (or Crust's shivyc: experimental)
```

There is no garbage collector, no .NET class library and no Qt. The C# standard library is replaced by a small
corelib (`corelib/`) written for Crust and [coost](https://github.com/crustos/coost). **Crust and coost are
required.** They live beside this repository, and `build.py` clones them for you:

```
parent/
  CCSharp/    this repository
  crust/      https://github.com/brentharts/crust    cpprust: C++ subset -> C
  coost/      https://github.com/crustos/coost       strings, files, paths and clocks
```

## Quick start

```sh
python3 build.py                        # clone crust + coost, build everything, test, run the examples
python3 build.py run path/to/your/cs    # compile and run a C# program
```

You need `python3`, `git` (only to clone the two dependencies), a C compiler (`cc` or `gcc`), and the **.NET SDK 8 or
later**. The compiler is built with the Roslyn that ships inside the SDK, so nothing is downloaded from NuGet and
everything works offline once the two dependencies are present. It has only been run on Linux.

## Using build.py

`python3 build.py -h` prints this reference. Commands come first, then options; the order of options does not matter.

### Commands

| command | what it does |
|---|---|
| *(none)* / `all` | `deps`, `compiler`, `coost`, `corelib`, `test`, `examples` |
| `deps` | clone `../crust` and `../coost` if they are missing. The only step that needs a network. |
| `compiler` | build `build/compiler/ccs.dll` from `compiler/src`. Skipped when nothing changed. |
| `coost` | build coost with its own `build.py` (`--test` also runs coost's tests) |
| `corelib` | check the corelib: the C# compiles, the native helpers lower through cpprust and compile |
| `test [names..]` | the input tests, then every case in `crust/tests` compared with real .NET |
| `convert INPUT.. [-o DIR] [--c]` | write the generated C++ (and with `--c` one self-contained C file) |
| `compile INPUT.. [-o EXE]` | build a native executable. Default: `build/bin/NAME` |
| `run INPUT.. [-- ARGS..]` | compile and run; everything after `--` goes to the program. Exits with the program's status. |
| `status` | where the dependencies, the .NET SDK, the compiler and the C compiler were found |
| `clean` | remove `build/` (the dependencies are left alone) |

### Inputs: files, folders, projects, solutions

`convert`, `compile` and `run` take **any number of inputs, in any mix**. Together they are **one program**, so you
can convert an entire C# project (or several) in a single command:

```sh
python3 build.py run     path/to/cs                             # a folder
python3 build.py run     Hello.cs                               # one file
python3 build.py compile src/ extra/Helpers.cs tools/Util.cs    # folders and files together
python3 build.py compile 'src/**/*.cs'                          # a wildcard (when your shell leaves it alone)
python3 build.py compile MyApp/MyApp.csproj -o myapp            # a project
python3 build.py convert Everything.sln -o generated --c        # a whole solution
```

| input | what it contributes |
|---|---|
| a `.cs` file | itself |
| a folder | every `.cs` under it, recursively, except `bin/`, `obj/`, `.git/` and `.vs/` |
| a `.csproj` (SDK-style) | every `.cs` under the project's folder, except `bin/`, `obj/` and folders that hold another project; then `<Compile Include>` adds, `<Compile Remove>` removes (wildcards and `..\` paths work), and every `<ProjectReference>` is followed, transitively |
| a `.csproj` (old-style) | only its `<Compile Include>` items |
| a `.csproj` with `<EnableDefaultCompileItems>false` | only its `<Compile Include>` items |
| a `.sln` / `.slnx` | the `.csproj` projects it lists, merged into one program |
| a wildcard | the `.cs` files it matches |

The files are sorted and de-duplicated. Two files with the same name in different folders (`App/Util.cs`,
`Lib/Util.cs`) are fine: each is named by its path under the common folder, and diagnostics always show your original
paths and line numbers. `Condition="..."` attributes in a project are not evaluated; every item is taken. A solution
merges **all** of its projects into one program, so give a `.csproj` instead if you want only one of them (its
references still come along).

### Choosing the entry point

The program's entry point is a `static int Main()`, `static void Main()` or `Main(string[] args)`. If a program has
more than one (typical for a solution with a test project), CC# refuses to guess:

```
Error: 2 entry points: App.Program.Main, Tests.Runner.Main
  Say which one with --main=Class.
```

```sh
python3 build.py run Everything.sln --main App.Program
```

### Options

| option | meaning |
|---|---|
| `-o PATH` | the output: the executable (`compile`) or the folder (`convert`) |
| `--main CLASS` | the class whose `static Main` is the entry point. Use the dotted name, namespace included. |
| `--name NAME` | the program's name (default: from the first input: `App.csproj` gives `App`, `src/` gives its parent's name) |
| `--c` | `convert`: also write the lowered C |
| `--cc CC` | the C compiler (default: `$CC`, else `cc`, else `gcc`) |
| `--debug` | compile with `-g -O0` instead of `-O2` |
| `--shivyc` | use Crust's own compiler instead of gcc (experimental, see below). With `test`: also run every case through shivyc and require agreement. |
| `--offline` | never touch the network. A missing dependency is an error that tells you what to clone. |
| `--update` | `deps`: `git pull --ff-only` the dependencies |
| `--no-test` | with no command: skip the test step |
| `--test` | `coost`: also run coost's own tests |
| `--` | everything after it is passed to the program (`run`) |

Environment: `CRUST_HOME` and `COOST_HOME` (where the dependencies are; default `../crust` and `../coost`),
`DOTNET` (the `dotnet` executable), `CC` (the C compiler).

### What you get

`compile` writes the executable (default `build/bin/NAME`) and, beside it, the generated C (`NAME.c`).

`convert` writes a folder (default `build/convert/NAME`):

```
generated/
  App_Program.cpp      one C++ file per C# file, named by its path under the common folder
  App_Util.cpp         (App/Program.cs, App/Util.cs, Lib/Cart.cs ... : slashes become underscores)
  Lib_Cart.cpp
  Shop.main.cpp        includes them in dependency order and defines main()
  Shop.c               with --c: the whole program as ONE C file
```

* The `.cpp` files are the C++ subset for **Crust**. A statement on C# line N is on line N of its file (a `#line`
  puts a moved type back), so a Crust diagnostic points at your `.cs`.
* `NAME.c` is **self-contained**: the coost sources the program needs are already spliced in. It needs no headers
  from this repository, so any C compiler builds it: `cc -O2 NAME.c -lm -o app`. That is the file to ship, or to feed
  to a different toolchain.

### Examples

```sh
# everything in a solution, converted, then built with plain cc
python3 build.py convert Shop.sln --main App.Program -o gen --c
cc -O2 -w gen/Shop.c -lm -o shop && ./shop

# a program that takes arguments
python3 build.py run Greeter.cs -- Ada Lovelace

# a debuggable build with another compiler
python3 build.py compile MyApp.csproj --cc clang --debug -o myapp

# offline, in CI: dependencies must already be there
python3 build.py --offline
```

### Errors

A program outside the supported subset is **refused**, naming the C# file and line, the reason, and what to write
instead. All refusals are reported, not just the first, and the exit status is 1:

```
Calc.cs:12: initialising `b` from another object would alias it: a class is single-owner here, so there is one
owner and no copy. Construct a new one with `new`, or pass the original by reference.
Calc.cs:30: `System.String.PadLeft` is declared in the CC# corelib (the .NET surface) but has no Crust implementation yet.
CCSharp --crust: 2 construct(s) outside the Crust C# subset
```

Ordinary C# errors (a typo, a missing type) are reported first, in Roslyn's own words. A bad input
(`no such file or folder: x.cs`, `... lists no .csproj projects`) says so and exits 1.

## gcc, and shivyc

**gcc is the default** for `compile`, `run` and `test`; use `--cc` or `$CC` for another C compiler. Crust has its own
C compiler, `shivyc`, and CC# output can go through it with `--shivyc`:

```sh
python3 build.py run examples/example1/src --shivyc
python3 build.py test --shivyc          # every case must give the same output under shivyc as under gcc
```

It is **experimental and off by default**. Every test case, and both examples, produce identical output under shivyc
and gcc except two: shivyc does not bundle glibc's `<errno.h>`, which coost's file and time code needs, so a program
that uses `File`, `Directory`, `Path`, `Stopwatch` or `Environment.TickCount64` cannot be compiled by it. With
`test --shivyc` those cases are reported as `gcc only`.

## What a C# program means here

The semantics are Crust's, not .NET's. `crust/README.md` has the full mapping.

* **A class is single ownership**: a value, destroyed at scope exit. Class, array and `List<T>` parameters are
  borrowed. `Node b = a;` would alias, so it is refused.
* **`string` is coost's `fastring`**, held by value. C# strings are immutable, so a copy cannot be told from sharing.
  There is no `null` string and no `char` (a C# `char` is UTF-16).
* **There is no `catch`.** An exception can never be observed, so a failure that .NET would throw
  (`int.Parse("x")`, `Substring` out of range) is an unhandled exception here too: the process ends, with the same
  exit status and the output printed so far.
* **Arithmetic wraps**, as in C#'s default unchecked context. Evaluation order is C#'s (left to right); where C
  could differ, CC# evaluates into temporaries in order, or refuses.
* Array and `Dictionary` indexing is unchecked, as in C.

## Native and managed: `--dna`

Some C# cannot be lowered to the Crust subset (lambdas, `try`/`catch`, generic methods, LINQ ...). With `--dna` such a class is not an
error: it stays C#, is compiled to CIL, and runs on [DotNetAnywhere](https://github.com/crustos/DotNetAnywhere) (DNA), a small .NET runtime in
C, linked into the same executable. No .NET installation is needed to run the result. The rest is still lowered to C, and the two sides call each other.

```sh
python3 build.py deps --dna                     # clones ../DotNetAnywhere (needs mono-mcs too: it builds DNA's corlib)
python3 build.py run --dna Prog.cs              # or compile / convert / test
```

**The unit is the class.** Each class is *native* (Crust C++, then C) or *managed* (C#, run by DNA). By default a class is tried as native, and
if Crust refuses it, it becomes managed and the reason is printed. Two attributes (in the `Crust` namespace, or a marker class of your own
with that name) override the default:

```csharp
[Managed] class Script { ... }   // never lowered, even if it could be
[Native]  class Kernel { ... }   // must be lowered: a refusal is an error, never a silent fall back
```

A class, its base class and its interfaces must be on the same side, so one managed member makes the whole family managed (a `[Native]`
member of such a family is an error). Enums and attribute classes exist on both sides; delegates are managed. `Main` may be on either side.

**What crosses the boundary** is what C can hold: `bool`, the integer types, `float`, `double`, `string` (UTF-8) and one-dimensional arrays of
the numeric types (copied in, and copied back after the call, so the callee's writes are seen; `bool[]` is not allowed). Between the two sides
you can call static methods with those types, and in one direction, objects:

* **Native code may hold objects of managed classes.** It can construct one, call its methods, read and write its properties, and pass it to
  managed methods (`Counter c = new Counter(5); c.Add(2); c.Count = 3; Holder.Use(c);`). The native class that stands for the managed one
  owns a *handle*: a number for which DotNetAnywhere's runtime keeps the object alive, together with everything the object refers to. The
  constructor makes it and the destructor releases it, so **the managed object lives exactly as long as the scope of the native variable that
  holds it** (a native class is a value, destroyed at scope exit). Run a program with `CCS_CHECK_HANDLES=1` and it reports, and exits 71,
  if it ends with a handle that was never released.
* **Managed code may not use an instance of a native class** (a variable, `new`, a method, a field), only call its static methods.

Everything else is refused where it is used, once for each pair of classes, saying what to do: a field of a managed object (make it a
property), an indexer, a user-defined operator, `ref`/`out`, a generic or overloaded method, a managed class with a base class, derived
classes or an interface of the program, a struct, a method that *returns* an object, a managed object passed *from managed code to native
code*, or any other type. A native method that returns an array cannot be called from managed code yet (return a string, or fill an array
parameter), and a call from managed code to C takes at most 6 C arguments (an array is two).

Calls nest freely (native calls managed calls native ...), and a static field keeps its value from call to call on either side. A managed
method called from native code must not block (`Sleep`, a lock, I/O); one that throws and is not caught ends the process, as in Crust.

How it is made, for each program (see `compiler/src/Partition.cs` and `Bridge.cs`, and `native/src/Host.h` in DNA):

| piece | what it is |
|---|---|
| `NAME.partition.json` | every class, its side and why, and every method that crosses |
| `managed/*.cs`, `NAME.managed.dll` | the managed classes, line for line as in your files, compiled by Roslyn against DNA's own `corlib.dll` |
| proxy classes in the C++ | `Script::Go(..)` and `Counter c(5); c.Add(2)` where native code uses managed ones: the call sites are unchanged; a proxy for an object owns its handle |
| `__ccs_bridge` in the managed C# | the static methods (`Counter_Add(self, ..)`) through which DNA, which calls only static methods, reaches an instance |
| `ccs_x_*` in the C++ | the native methods that managed code reaches with `[DllImport]`, with an FFI manifest (`NAME.ffi.json`) so that is a direct call |
| `NAME.bridge.c` | the C that marshals into DNA's host API (and `main`, when `Main` is managed) |

The executable finds `NAME.managed.dll` and `corlib.dll` beside itself, wherever it is started. `--dna` has only been run on Linux.

A host with its own native side can use just the managed half: `dotnet build/compiler/ccs.dll --managed-compile OUT.dll CORLIB.dll FILE.cs ...` compiles C# with Roslyn against DNA's `corlib.dll` and nothing else (exit status 0 on success, errors on stderr). Crust's `unity_pack --hybrid` / `--managed` use it.

### WebAssembly: `--wasm`

`--wasm` builds the same program for `wasm32-wasi` instead of the host. It implies `--dna`, so a program with managed classes gets DNA linked
in (compiled for wasm, with its wasm JIT), and a program with nothing managed is plain wasm. It cannot be combined with `--shivyc`.

```
python3 build.py deps --wasm                    # as --dna; also checks clang, lld, wasi-libc, llvm-ar and node
python3 build.py run --wasm Prog.cs             # or compile / convert / test
python3 crust/ccs2c.py --wasm Prog.cs           # the same without build.py
```

You get `NAME.wasm`, `NAME` (a launcher script), `run_wasm.mjs` (the node WASI host, which also provides the JIT), and for a hybrid program
`NAME.managed.dll` and `corlib.dll`, all kept together. Run it with `./NAME args...` (or `node run_wasm.mjs --app NAME.wasm args...`). The stack
is 8 MB. Exit statuses and output are the same as the native build's; an uncaught trap ends the process like abort (SIGABRT).
`CCS_WASM=1 python3 crust/run_tests.py` (or `build.py test --wasm`) runs the whole suite this way and compares with .NET as usual.

## Reference classes: `[MaxInstances(N)]`

A class is single-owner here, so a graph of objects that refer to one another (a scene of game objects and their components) needs another kind of
class. `[MaxInstances(N)]` makes one: it has N statically allocated slots, and a reference to it is a plain pointer. Assignment copies the pointer,
`null` is 0, `==` compares addresses, and a reference may be stored in a field, a `List<T>` or a `T[]` and passed and returned freely. `new T(..)` takes
the next slot; nothing is freed one at a time, and an (N+1)th live object aborts. Declare the marker class yourself (`class MaxInstancesAttribute : Attribute
{ public MaxInstancesAttribute(int n) { } }`), as the tests do.

**Inheritance.** An arena class may derive from **one other arena class**, to any depth, with `virtual` / `override` / `abstract` as for any class:

```csharp
[MaxInstances(64)] abstract class Component { public int Id; public virtual void Update() { } }
class Mover : Component { public override void Update() { Id += 1; } }          // an arena class too: 64 slots, its own
[MaxInstances(8)] class Spinner : Component { public override void Update() { Id += 2; } }
```

* A class that derives from an arena class **is** an arena class. It has slots of its own (each class of a family has its own arena); its N is the one it
  declares, or else its base's. An abstract base never makes objects, so its N only sets that default.
* A reference to a derived class is also a reference to its base: `Component c = new Mover();`, `list.Add(mover)`, passing it as a `Component`, and
  `c.Update()` reaches `Mover.Update` through the vtable. The object begins with its base, so the pointer is one address; the C# conversion is written as a cast
  in the generated C, which therefore compiles cleanly with compilers that refuse an implicit pointer conversion (gcc 14).
* `base == derived` compares addresses. Between arena classes `c is Mover`, `c as Mover` and `(Mover)c` ask the object its type (cpprust `--rtti`: a descriptor in
  each class's vtable, switched on by the program saying `dynamic_cast`): `as` gives null on a miss, and so does a cast, where C# would throw. For any other
  type they are refused as everywhere (use a virtual method).
* **Giving a slot back.** There is no GC and no `delete` in C#, so an engine that wants to reuse slots says so: a static class named `Arena` with
  `public static void Free<T>(T o) where T : Base { }` (the body is for .NET). A call `Arena.Free(x)` is written as `Base__free(x)`, which finds the arena the
  object lives in by its address, drops it, zeroes it, and makes its slot the next one `new` takes. Freeing an object twice, or using it after, is the program's bug.
* A **generic method** is allowed when every type parameter is constrained to an arena class and the method is not virtual: `T Get<T>() where T : Part`. It
  is emitted as a C++ member template, instantiated per type argument, so `c as T` and `new T()` (with `where T : Part, new()`) work inside it.
* Not yet: an arena class that implements an **interface**, a base class that is **not** an arena class, a **generic** arena class, and `base.M()`
  (a call to the base's implementation; the subset refuses it for every class). Each is refused with a message saying so.
* With `--dna`, a family is on one side: if one class of it cannot be lowered, the whole family is managed (`in the inheritance family of managed X`).

## The corelib

`corelib/src` is C# declarations compiled by Roslyn **instead of** the .NET reference assemblies, so it is the whole
of what a program can name. A member without a body is implemented in C++ (`corelib/native`, on coost):

```csharp
[Cpp("cs_s_substr1({this}, {0})")]  public extern string Substring(int startIndex);
```

A member declared without `[Cpp]` is part of .NET that is not implemented yet: using it is refused, by name.

| area | implemented |
|---|---|
| `string` | `Length`, `Substring`, `IndexOf`, `LastIndexOf`, `Contains`, `StartsWith`, `EndsWith`, `Replace`, `ToUpper`, `ToLower`, `Trim`, `Equals`, `IsNullOrEmpty`, `+`, `==`, `$"..."` |
| text | `StringBuilder` (`Append`, `AppendLine`, `Clear`, `Length`, `ToString`; chains work) |
| numbers | `Math` (`Abs`, `Min`, `Max`, `Sign`, `Sqrt`, `Pow`, `Floor`, `Ceiling`, `Sin`, `Cos`, `PI`, `E`), `int` / `long` `Parse`, `TryParse`, `ToString`, `Convert` |
| collections | `T[]`, `List<T>`, `Dictionary<K,V>` with an integer, bool or enum key |
| I/O | `Console.Write` / `WriteLine`, `File`, `Directory`, `Path` |
| other | `Stopwatch`, `Environment.TickCount64` / `NewLine` / `Exit` |

Refused today, with a message: `char`, `null`, `throw` / `try`, lambdas and delegates, generic methods (except on arena classes, above), `is` / `as` (except between arena classes),
LINQ, `goto`, `params`, named arguments, nested types, operators on a class, array initialisers, printing
a `float` / `double`, and `Dictionary<string, ...>`.

Optional arguments are supported: a call that leaves one out gets the declared default (a number, a `bool`, an enum member,
a string, `default(struct)`, or `null` for an arena class). A default of `null` for a string or for an owned class is refused:
neither has a null here. Operators are supported on a **struct**: `a + b`, `-a`, `a == b`, `a += b` are calls of the operator's
static method (`op_Addition`; several of one name are told apart by their parameter types). A class has identity, so
`a + b` would have to copy or alias it, and is refused. A struct that declares only constructors with parameters still has a
parameterless one, as in C#. The program is parsed with `CRUST` defined, so a library can keep what the subset cannot take
behind `#if !CRUST`.

### Unity types: `--unity-types`

`--unity-types` makes the UnityEngine value types part of the program: `Vector2`, `Vector3`, `Quaternion`, `Color`, and the enums `ForceMode2D`, `RigidbodyType2D`
and `Space`, in namespace `UnityEngine` (`unity/*.cs`, a plain C# source that is added to the inputs). A game says `using UnityEngine;` and carries no copy.
Their operators are ordinary struct operators, so `a + b`, `-a`, `a * 2f`, `2f * a`, `a == b`, `a += b`, `q * v` become calls of the operator's static method
(`Vector2::op_Addition(a, b)`), as above; the same file compiled by .NET gives the same numbers. So are the conversions between `Vector2` and `Vector3`: a struct
may declare `implicit operator` / `explicit operator`, and `Vector3 p = new Vector2(1f, 2f)`, `(Vector3)v` and passing a `Vector2` where a `Vector3` is wanted are calls
of its `op_Implicit` / `op_Explicit`. What the subset cannot take (`Equals`, `GetHashCode`, `ToString`) is behind `#if !CRUST`. `python3 crust/ccs2c.py Game.cs --unity-types --run`; a test case selects it with a
`// unity-types` first line (`crust/tests/unity_types.cs`).

### A small Unity engine: `--unity-engine`

`--unity-engine` adds `unity-engine/*.cs` to the program (and `--unity-types`, which it builds on): a small UnityEngine written in the subset, so it lowers to C
like the rest, and also runs on .NET, so it is tested against it. It is the first slice of an engine that a Unity project's scripts can run on:

| | |
|---|---|
| `GameObject`, `Transform`, `Component` | objects are [arena classes](#reference-classes-maxinstancesn): a reference is a pointer, with `null` and `==`; `transform.position` is a property |
| `MonoBehaviour` | a script is a class that derives from it. Unity finds `Update` by name; there is no reflection here, so the messages (`Awake`, `Start`, `Update`, `LateUpdate`) are `virtual` and a script *overrides* them |
| `SpriteRenderer`, `Camera`, `Time` | what a 2D frame needs |
| `Engine` | the scene's objects, `Tick()` (Awake / Start once, then Update, then LateUpdate) and `Find(name)` |

A scene is code: `new GameObject("Hero")`, then `go.Attach(component)` (the packer writes it from the scene file). `crust/tests/unity_engine_scene` is a scene with
two scripts that is built and run both ways. `GetComponent<T>()` and `AddComponent<T>()` are generic methods of `GameObject` (and `GetComponent<T>()` of `Component`), and
`crust/tests/unity_engine_components` uses them. `Instantiate` and `Destroy` are methods of `Component` (so a script calls them bare): a prefab is a `Prefab` subclass with a `Create()` that builds the
GameObject (the packer writes one per prefab), and `Instantiate(template)` calls it. `Destroy` takes effect at the end of the frame and gives the slots back
(`Arena.Free`, below), so a game can make far more than N objects over its life as long as no more than N are alive at once. Not yet: physics, the rest of Unity.

`--describe=FILE` (compiler) stops after Roslyn has checked the program and writes its classes, bases and public fields with their types as JSON. A tool that writes a
scene as code asks it which script declares `speed`, and of what type, instead of reading the C# itself.

## Tests

```sh
python3 build.py test                  # the input tests, then every case, with gcc
python3 build.py test corelib_string   # just the cases whose names are given
python3 build.py test --shivyc         # ... and with Crust's own compiler too
```

* `crust/test_inputs.py` tests what counts as a program: files, folders, `.csproj` and `.sln`, and converting several
  inputs as one.
* `crust/test_partition.py` tests `--dna`'s partition and bridge: what goes native or managed, the diagnostics, and the files written.
* `crust/tests` holds a case per file or folder. Each runs through CC# and gcc, and its **stdout and exit status are
  compared with the same C# run on real .NET** (when `dotnet` is available). A case that is a folder holding a `.sln` or
  `.csproj` is converted *as that project*. `refuse_*.cs` cases pin a refusal's message. `// main: Class` in a case's
  first file selects its entry point. A `// dna` case is built with `--dna` (and skipped, loudly, where DotNetAnywhere or `mcs` is missing); it
  defines its own `class ManagedAttribute : Attribute {}`, so it is also an ordinary C# program for the .NET it is compared with.
* A line check asserts that every `return` is on its C# line in the generated C++.

## Layout

```
build.py            builds, tests, and runs everything (this file's commands)
compiler/src        the compiler: CCSharpCompiler.cs (driver) and CrustEmitter.cs (the Crust back end);
                    --dna: Partition.cs (native or managed), Bridge.cs (what crosses), ManagedBuild.cs (managed C# to CIL)
corelib/src         the C# class library      corelib/native   its C++ side, on coost
crust/              ccs2c.py (the pipeline), inputs.py (files/projects), unit.py (coost splicing), tests, docs
examples/           example1 (hello), example2 (a List<string> benchmark)
build/              what build.py makes (the compiler, executables, converted output). Not checked in.
```

`crust/ccs2c.py` is the pipeline `build.py` drives. It can be used directly (`python3 crust/ccs2c.py -h`).

## History

This was the Q# successor written by Peter Quiring. The original notes on the compiler are still true:

The C# to C++ compiler is written in C# and uses the amazing Roslyn project to analyze code. I've tried MANY times to
use Java's compiler to build a similar project but it just doesn't expose enough symbol information, and converting
Java to C++ or C is impossible (even tried x64 assembly). The Java language is just too abstract to implement. Just
look at the problems Oracle is having with Graal.

The old back end emitted garbage-collected C++ over a corelib built around `System.Object`, UTF-16 strings, reflection
and threads. None of that exists in Crust, and it was removed (it remains in the git history). What survived is the
shape: the same namespaces and type names, and `Console`, `File` and `Environment`.

Author : Peter Quiring (pquiring at gmail dot com)
Crust back end : https://github.com/brentharts/crust
