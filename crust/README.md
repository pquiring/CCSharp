# CC# `--crust`: the Crust back end

CC# turns C# into the **C++ subset that [Crust](https://github.com/brentharts/crust) accepts**. `cpprust` lowers
that to C, and gcc (the default) or Crust's own `shivyc` (experimental, opt-in) compiles it. Crust and
[coost](https://github.com/crustos/coost) are required; `python3 build.py` (top level) clones them beside this repo
and builds everything offline. **The top-level `README.md` is the user guide** (`build.py`, inputs, options); this
file is about the pipeline and the language mapping.

```
C#  --Roslyn-->  Crust C++ subset  --cpprust-->  C  --shivyc / cc-->  native
     (CC#)        + coost spliced in
```

```sh
python3 build.py                                      # everything
python3 build.py test                                 # every case in crust/tests vs real .NET (gcc)
python3 build.py run App.csproj -- arg1 arg2          # files, folders, .csproj, .sln: any number, one program
python3 crust/ccs2c.py INPUT.. --exe out [--shivyc]   # the pipeline directly; see its --help
CCSharpCompiler FIRST.cs Name --srclist=FILES.txt --home=REPO [--main=Class]   # what ccs2c runs
```

The compiler takes the program as `--srclist=FILE` (one `.cs` path per line); `ccs2c.py` / `build.py` write that list
from whatever you pass (see `inputs.py`). With no list, `CCSharpCompiler DIR Name` reads every `.cs` under `DIR`.
It writes `cpp/*.cpp` (one per C# file, named by its path under the common folder, `dir/Util.cs` -> `dir_Util.cpp`)
and `cpp/Name.main.cpp`, which includes them in dependency order and defines `main`.

## The files here

| file | what |
|------|------|
| `ccs2c.py` | the pipeline: inputs -> compiler -> unit -> cpprust -> gcc (or shivyc), and the `to_cpp` / `to_c` / `build_run` / `convert` API |
| `inputs.py` | what a program is made of: files, folders, `.csproj` (default items, `Compile Include/Remove`, `ProjectReference`), `.sln` |
| `test_inputs.py` | unit tests for `inputs.py`, and for converting several inputs as one program |
| `unit.py` | assembles a program's translation unit: the coost sources its headers reach are spliced in beside it (the rule coost's own build.py uses), then lowered in-process with cpprust's `any_order` (C# lets a type use one declared below it) |
| `run_tests.py` | the test runner (gcc; `SHIVYC=1` adds shivyc) |
| `tests/` | `NAME.cs` (or a folder, or a folder with a `.sln` / `.csproj`) per case; `refuse_*.cs` pin a refusal; `// refuse: text`, `// expect-rc: N`, `// main: Class` headers |

## What a program means

Same decisions as Crust's own C# front end (`CSRUST.md`), so CC# and `csrust` agree. **There is no GC.**

| C# | emitted |
|----|---------|
| `class` / `struct` / `interface` | `class`; an interface is pure virtual |
| `Counter c = new Counter(1)` | `Counter c(1);` a class is a single-owner value |
| class / array / `List<T>` / `StringBuilder` parameter | `T &` (borrowed) |
| `ref` / `out` parameter | `T &`; the call site passes the variable |
| `enum K : byte { A }` | `enum K_values { K_A }; typedef unsigned char K;` |
| property | field + `get_P()` / `set_P(v)`; `x.P += e` -> `x.set_P(x.get_P() + e)` |
| `T[]`, `List<T>` | `std::vector<T>` |
| `string` | coost `fastring` by value; immutable in C#, so a copy is unobservable |
| `a + b + 5`, `$"..{x}.."`, `s += x` | built with `append_cstr` / `append_str` / `append_int` onto a named local |
| `s == "lit"` | `strcmp(a.c_str(), "lit")`, which works inside a condition |
| `Console.Write/WriteLine` | one `printf`; if any value has a side effect they are snapshotted in order first |
| a method that does `return this;` | returns `T *`, chains with `->` (Crust refuses reference returns) |
| a `Derived` passed as `Base` | a `Base &` bound through an explicit cast (cpprust only upcasts `new`; shivyc rejects the bare pointer) |
| `a << n` | count masked to the operand width, as C# does |
| `f(new T(x))` | `T _t1(x); f(_t1);` (Crust needs an address), only when evaluation order cannot change |

Arithmetic wraps. Constant expressions are folded by Roslyn (`int.MaxValue`, `1 << 33`, enum constants).
Line numbers are preserved: a statement on C# line N is on line N of the emitted C++, with a `#line` where a
type had to move (a base declared after its derived class).

## The corelib (`../corelib`)

The corelib is C# source compiled by Roslyn instead of the .NET reference assemblies, so **it is the whole of
what a program can name**. Members are mapped to C++ with attributes (`corelib/src/Crust/Cpp.cs`):

```csharp
[Cpp("cs_s_substr1({this}, {0})")]   public extern string Substring(int startIndex);
[Cpp("std::vector<{T0}>")]           public sealed class List<T> { ... }       // on a type
[Cpp("{this}.append_str({0})"), CppFluent]   public extern StringBuilder Append(string value);
```

`{this}` is the receiver, `{0}`.. the arguments, `{0:c}` an argument as a `const char *`, `{T0}` a type
argument. A member with no `[Cpp]` is the part of .NET that is not implemented yet: using it is refused by name.
The native side is `corelib/native/include/cs/*.h`, in the C++ subset, on coost. `build.py corelib` checks both.

Failures that .NET would throw (`int.Parse("x")`, `Substring` out of range) are unhandled exceptions here too:
there is no `catch`, so nothing can observe the difference, and the process ends with the same status.

## Refusals are the contract

Everything outside the subset is **refused at its C# line, with the reason and the replacement**, all of them
reported. Where C# would alias an object and a single-owner value would silently copy it, CC# refuses:

```
Case.cs:4: initialising `b` from another object would alias it: a class is single-owner here ...
Case.cs:5: returning an existing object would copy it where C# returns the same one ...
Case.cs:4: `System.String.PadLeft` is declared in the CC# corelib (the .NET surface) but has no Crust implementation yet.
```

Refused today (`tests/refuse_*.cs`): `char`, `null`, `throw` / `try`, lambdas and delegates, generic methods,
`is` / `as` / `??`, LINQ, `goto`, `params`, named / default arguments, nested types, `extern` in user code,
`: this(..)`, array / collection initialisers, printing a float, string fields with initialisers, a Dictionary
with a non-integer key, converting to a secondary base, and any corelib member without a `[Cpp]`.

Statements are hoisted ahead of the current one (string building, temporaries, upcasts) only when nothing else in
the statement could observe the reordering; otherwise it is refused, never reordered.

## Not yet

* `throw` / `try` -> Crust's checked `raise` / `except`; lambdas; `[Shared]` and `[MaxInstances(N)]` arenas (which
  also unlock `null` and reference identity); nested types; operators on a class (a struct's are supported); `char`; string `Split` / `Join`
  / `Format`; `Dictionary<string, ..>` (Crust orders map keys with a `compare` method fastring does not have).
* A string field is the empty string until assigned (C# has `null`).
* shivyc: coost's `fs` and `time` need `<errno.h>`, which it does not bundle, so programs using `File` /
  `Stopwatch` cannot be compiled by it (`test --shivyc` reports them as `gcc only`). Everything else agrees with gcc.
