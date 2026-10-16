/*
  CC# --dna : the bridge between the native and the managed classes.

  What crosses is what C can hold, in calls between the two sides:

      bool, byte, sbyte, short, ushort, int, uint, long, ulong, float, double      (a char is not in the Crust subset)
      string                                                                         (UTF-8 across the boundary)
      T[] of the numeric types above                                                 (copied in; copied back after the call)
      an object, as a handle (see below)
      ref / out of a numeric type                                                    (managed -> native only)
      void (a result only)

  Two kinds of object cross, and they are different things:

    * An object of a MANAGED class, held by native code through a handle that DotNetAnywhere's runtime keeps alive.  A native proxy class
      stands for it and owns the handle (made by its constructor, released by its destructor: the managed object lives as long as the
      native variable's scope).  Calls go through generated static methods on the managed side (`Script_Go(self, ..)`).  The class must
      be a plain class: no base class, derived class, struct, generics, fields (use a property).

    * An object of a NATIVE ARENA class (`[MaxInstances(N)]`), used by managed code.  An arena object has a fixed address for the whole
      program and is never freed on its own, so its address IS its handle: there is nothing to own or release, and `null` is 0.  A managed
      proxy class holds the address; one proxy per address (a cache), so `a == b` and `a == null` mean what they do in C#.  Its fields,
      properties, methods and constants are all reachable, `new` makes one, and it may be passed and returned, both ways.

  A native class that uses a managed class in a way that cannot cross is not an error: it becomes managed too (callers follow the classes
  they call; a callee is never pulled across), unless it is marked [Native].  That is what keeps an engine native and lets the code that
  uses a script follow the script to the managed side.

  Each boundary member is described once (BridgeMethod) and the pieces that make it work are written from that:

      native -> managed      C++ proxy class `Script { Script(int); ~Script(); int Go(int); static .. }`, calling `ccs_b_Script_Go(handle, ..)`
                             C glue  `ccs_b_Script_Go`: marshals into DNA_Call (DotNetAnywhere's host API, native/src/Host.h)
                             C# trampolines (`__ccs_bridge`): the static methods that DNA calls: `Script_Go(Script self, int a0)`

      managed -> native      C++ export `ccs_x_Node_SetPosition(long long self, ..)`: takes what C holds, rebuilds the strings, arrays and objects
                             Crust needs, and does what the C# did
                             FFI manifest: the export, for DotNetAnywhere's `build.py --ffi`, so a [DllImport] of it is a direct call
                             C# proxy class `Node { public void SetPosition(float x, float y) }` (what the managed code compiles against)

  A call with an array takes two C parameters (the pointer, the length); DotNetAnywhere's FFI has no limit (past the registers C has the rest on the stack), and the call from C to managed code takes at most 16.
*/
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CCSharpCompiler;

enum Dir { NativeToManaged, ManagedToNative }

/** What a call is: a static method, a constructor, an instance method, a property's accessor, or a field read / write. */
enum MKind { Static, Ctor, Instance, Getter, Setter, FieldGet, FieldSet }

/** `[MaxInstances(N)]`: found by name, as the emitter finds it (a program may declare its own marker class).  The one place that says what an arena class is:
    the emitter, the piece order and the native/managed bridge all ask here.
    A class that derives from an arena class is an arena class too, with its own slots: its N is the one it declares, else its base's. */
static class ArenaClass
{
  /** N of the class's own `[MaxInstances(N)]`, or null when it has none (an invalid N, zero or less, is returned as it is: the emitter refuses it). */
  public static int? OwnSize(INamedTypeSymbol n)
  {
    foreach (var a in n.OriginalDefinition.GetAttributes())
      if (a.AttributeClass != null && a.AttributeClass.Name == "MaxInstancesAttribute" && a.ConstructorArguments.Length == 1
          && a.ConstructorArguments[0].Value is int v)
        return v;
    return null;
  }

  /** the number of slots the class's arena has; 0 when it is not an arena class */
  public static int Size(ITypeSymbol t)
  {
    var n = t as INamedTypeSymbol;
    if (n == null || n.TypeKind != TypeKind.Class) return 0;
    n = n.OriginalDefinition;
    int? own = OwnSize(n);
    if (own.HasValue) return own.Value > 0 ? own.Value : 0;
    if (n.BaseType != null && n.BaseType.SpecialType == SpecialType.None) return Size(n.BaseType);
    return 0;
  }

  public static bool Is(ITypeSymbol t) { return Size(t) > 0; }

  /** does `t` derive (however far) from the arena class `baseType`? */
  public static bool DerivesFrom(INamedTypeSymbol t, INamedTypeSymbol baseType)
  {
    for (var b = t.BaseType; b != null; b = b.BaseType)
      if (SymbolEqualityComparer.Default.Equals(b.OriginalDefinition, baseType.OriginalDefinition)) return true;
    return false;
  }
}

/** What a type is, at the boundary. */
class BType
{
  public char Kind;              //i l f d s b o a v  (o: a managed object's handle; a: a native arena object's address)
  public string Cs;              //C#: bool byte ... string, int[], global::Ns.Class
  public string Cpp;             //C++ spelling of the value (of an element, for an array; the class, for an object): "unsigned char", "fastring"
  public string C;               //C spelling at the boundary: `int` for a bool; the element type for an array; `long long` for an object
  public int Elem;               //array: bytes in an element
  public string Manifest;        //the FFI manifest's name for the scalar (or element)
  public bool IsBool, IsString, IsArray, IsVoid, IsObject, IsArena;
  public string CsElem;

  public bool Scalar { get { return !IsString && !IsArray && !IsVoid && !IsObject && !IsArena; } }
  /** The letter DotNetAnywhere's host API knows: a native object is an address, a number. */
  public char DnaKind { get { return Kind == 'a' ? 'l' : Kind; } }
}

class BridgeParam
{
  public string Name;            //as the C# declares it
  public BType Type;
  public RefKind Ref;            //ref / out (a numeric parameter, to native code)
}

/** A `const` of a native class: the managed side cannot fold it, because the class is not there, so its proxy carries the value. */
class ConstField
{
  public ClassPlan Owner;
  public IFieldSymbol Field;
}

class BridgeMethod
{
  public ClassPlan Owner;
  public ISymbol Symbol;         //the method, or the field
  public Dir Dir;
  public MKind Kind;
  public bool IsStatic;
  public int CtorIndex;          //Kind == Ctor: which of the constructors used (a name of its own on each side)
  public string Flat;            //the class, as Crust spells it: Ns_Cls
  public string CppName;         //the member, as Crust spells it (get_X / set_X for a property)
  public List<BridgeParam> Params = new List<BridgeParam>();
  public BType Ret;
  public string Ns;              //C# namespace ("" for none)
  public string Cls;             //C# class name
  public string Name;            //C# member name
  public string CsClass;         //global::Ns.Cls
  public string Access;          //public / internal: the C# accessibility the member has
  public string Template;        //a foreign C function ([Crust.Cpp("pb2_x({0})")] extern): what a call spells

  public bool HasSelf { get { return !IsStatic && Kind != MKind.Ctor && Kind != MKind.Static; } }
  /** The part of the C name after the class: `Go`, `get_Count`, `_new0`. */
  public string Member { get { return Kind == MKind.Ctor ? "_new" + CtorIndex : Kind == MKind.Getter || Kind == MKind.FieldGet ? "get_" + Name : Kind == MKind.Setter || Kind == MKind.FieldSet ? "set_" + Name : Name; } }
  public string CName { get { return (Dir == Dir.NativeToManaged ? "ccs_b_" : "ccs_x_") + Flat + "_" + Member; } }
  /** A call into managed code that DNA cannot make directly: an instance member (DNA calls static methods), or a signature with a native object (DNA passes
      its address, and the managed side must make the proxy). */
  public bool NeedsTrampoline { get { return Dir == Dir.NativeToManaged && (Kind != MKind.Static || Params.Any(p => p.Type.IsArena) || Ret.IsArena); } }
  public string FindClass { get { return NeedsTrampoline ? "__ccs_bridge" : Cls; } }
  public string FindNs { get { return NeedsTrampoline ? "" : Ns; } }
  public string FindName { get { return NeedsTrampoline ? Flat + "_" + Member : Name; } }
  public string Sig { get { return (HasSelf ? "o" : "") + string.Concat(Params.Select(p => p.Type.DnaKind)) + ">" + Ret.DnaKind; } }
  public int FfiArgs { get { return (HasSelf ? 1 : 0) + Params.Sum(p => p.Type.IsArray ? 2 : 1); } }
}

class BridgePlan
{
  public List<BridgeMethod> Methods = new List<BridgeMethod>();
  public List<ConstField> Consts = new List<ConstField>();
  public List<string> Errors = new List<string>();
  public bool UsesString;
  public List<ClassPlan> Proxied = new List<ClassPlan>();        //managed classes that native code holds objects of
  public List<ClassPlan> ProxiedArena = new List<ClassPlan>();   //native arena classes that managed code uses objects of
  public SortedSet<string> Includes = new SortedSet<string>(StringComparer.Ordinal);   //headers the foreign functions in the exports need
  /** Native classes that use a managed class in a way that cannot cross, and why: they become managed too. */
  public Dictionary<ClassPlan, string> Demotable = new Dictionary<ClassPlan, string>();

  readonly PartitionPlan plan;
  BridgePlan(PartitionPlan p) { plan = p; }

  public IEnumerable<BridgeMethod> ToManaged { get { return Methods.Where(m => m.Dir == Dir.NativeToManaged); } }
  public IEnumerable<BridgeMethod> ToNative { get { return Methods.Where(m => m.Dir == Dir.ManagedToNative); } }

  // ------------------------------------------------------------------ types

  static readonly Dictionary<SpecialType, (string cs, string cpp, string c, char kind, int size, string manifest)> Prims =
    new Dictionary<SpecialType, (string, string, string, char, int, string)> {
      { SpecialType.System_Boolean, ("bool", "bool", "int", 'i', 4, "int") },
      { SpecialType.System_Byte, ("byte", "unsigned char", "unsigned char", 'i', 1, "byte") },
      { SpecialType.System_SByte, ("sbyte", "signed char", "signed char", 'i', 1, "sbyte") },
      { SpecialType.System_Int16, ("short", "short", "short", 'i', 2, "short") },
      { SpecialType.System_UInt16, ("ushort", "unsigned short", "unsigned short", 'i', 2, "ushort") },
      { SpecialType.System_Int32, ("int", "int", "int", 'i', 4, "int") },
      { SpecialType.System_UInt32, ("uint", "unsigned", "unsigned", 'i', 4, "uint") },
      { SpecialType.System_Int64, ("long", "long long", "long long", 'l', 8, "long") },
      { SpecialType.System_UInt64, ("ulong", "unsigned long long", "unsigned long long", 'l', 8, "ulong") },
      { SpecialType.System_Single, ("float", "float", "float", 'f', 4, "float") },
      { SpecialType.System_Double, ("double", "double", "double", 'd', 8, "double") },
    };

  const string AllowedTypes = "bool, byte, sbyte, short, ushort, int, uint, long, ulong, float, double, string, arrays of the numeric types, objects of arena classes, and (to a managed method) objects of managed classes";

  static string FlatOf(INamedTypeSymbol t)
  {
    string n = CrustEmitter.IdentOf(t.Name);
    if (t.ContainingNamespace != null && !t.ContainingNamespace.IsGlobalNamespace)
      n = t.ContainingNamespace.ToDisplayString().Replace(".", "_") + "_" + n;
    return n;
  }

  static string Global(INamedTypeSymbol t) { return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat); }

  /** Can native code hold objects of this managed class?  null if so, else why not. */
  string NotAnObject(ClassPlan cp)
  {
    var s = cp.Sym;
    if (cp.Kind != "class") return "`" + cp.Name + "` is a " + cp.Kind + ": only classes can be held by handle (a struct is copied, which a handle cannot do)";
    if (s.IsGenericType) return "`" + cp.Name + "` is generic";
    if (s.IsStatic) return "`" + cp.Name + "` is a static class";
    if (s.BaseType != null && s.BaseType.SpecialType != SpecialType.System_Object) return "`" + cp.Name + "` derives from `" + s.BaseType.Name + "`: a class hierarchy cannot cross the boundary yet";
    if (plan.Order.Any(o => o != cp && o.Sym.BaseType != null && SymbolEqualityComparer.Default.Equals(o.Sym.BaseType.OriginalDefinition, s)))
      return "`" + cp.Name + "` has derived classes: a class hierarchy cannot cross the boundary yet";
    if (plan.Order.Any(o => o.Sym.Interfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, s))))
      return "`" + cp.Name + "` is an interface";
    if (s.Interfaces.Any(i => plan.Get(i) != null)) return "`" + cp.Name + "` implements an interface of the program: a class hierarchy cannot cross the boundary yet";
    return null;
  }

  void ProxyArena(ClassPlan cp) { if (!ProxiedArena.Contains(cp)) ProxiedArena.Add(cp); }

  /** The boundary type of `t`, or null with the reason in `why`.  `dir`: which way the call goes; `isResult`: t is the result. */
  BType TypeOf(ITypeSymbol t, Dir dir, bool isResult, out string why)
  {
    why = null;
    if (t.SpecialType == SpecialType.System_Void) return new BType { Kind = 'v', IsVoid = true, Cs = "void", Cpp = "void", C = "void" };
    if (t.SpecialType == SpecialType.System_String) return new BType { Kind = 's', IsString = true, Cs = "string", Cpp = "fastring", C = "const char *", Manifest = "cstr" };
    if (Prims.TryGetValue(t.SpecialType, out var p))
      return new BType { Kind = p.kind, Cs = p.cs, Cpp = p.cpp, C = p.c, Elem = p.size, Manifest = p.manifest, IsBool = t.SpecialType == SpecialType.System_Boolean };
    if (t is IArrayTypeSymbol a) {
      if (a.Rank != 1) { why = "a multidimensional array cannot cross"; return null; }
      if (a.ElementType.SpecialType == SpecialType.System_Boolean) { why = "an array of bool cannot cross (use byte)"; return null; }
      var e = TypeOf(a.ElementType, dir, false, out why);
      if (e == null) return null;
      if (!e.Scalar) { why = "an array of `" + a.ElementType.ToDisplayString() + "` cannot cross"; return null; }
      return new BType { Kind = 'b', IsArray = true, Cs = e.Cs + "[]", CsElem = e.Cs, Cpp = e.Cpp, C = e.C, Elem = e.Elem, Manifest = e.Manifest };
    }
    if (t.SpecialType == SpecialType.System_Char) { why = "`char` is not in the Crust subset (use byte, or int for a code point)"; return null; }
    if (t is INamedTypeSymbol nt && plan.Get(nt) is ClassPlan cp && !cp.Shared) {
      if (cp.Final == Part.Native) {
        if (!ArenaClass.Is(nt)) { why = "an object of the native class `" + cp.Name + "` cannot cross: only an arena class (`[MaxInstances(N)]`) has an address that stays the same"; return null; }
        ProxyArena(cp);
        return new BType { Kind = 'a', IsArena = true, Cs = Global(nt), Cpp = FlatOf(nt), C = "long long", Manifest = "long" };
      }
      if (dir == Dir.ManagedToNative) { why = "an object of the managed class `" + cp.Name + "` cannot be passed to native code yet"; return null; }
      if (isResult) { why = "returning an object of `" + cp.Name + "` across the boundary is not supported yet (native code holds an object it constructed, and passes it)"; return null; }
      string no = NotAnObject(cp);
      if (no != null) { why = no; return null; }
      if (!Proxied.Contains(cp)) Proxied.Add(cp);
      return new BType { Kind = 'o', IsObject = true, Cs = Global(nt), Cpp = FlatOf(nt), C = "long long" };
    }
    why = "`" + t.ToDisplayString() + "` cannot cross";
    return null;
  }

  // ------------------------------------------------------------------ analysis

  static string Side(Part p) { return p == Part.Managed ? "managed" : "native"; }

  static string Access(ISymbol s)
  {
    switch (s.DeclaredAccessibility) {
      case Accessibility.Public: return "public";
      case Accessibility.ProtectedOrInternal: return "protected internal";
      default: return "internal";                //(what the managed side may use: it is the same assembly)
    }
  }

  /** The template of a foreign C function: `[Crust.Cpp("pb2_x({0})")] public static extern ..`, or null. */
  static string ExternTemplate(IMethodSymbol m)
  {
    if (!m.IsExtern) return null;
    foreach (var a in m.OriginalDefinition.GetAttributes())
      if (a.AttributeClass != null && a.AttributeClass.Name == "CppAttribute" && a.ConstructorArguments.Length == 1)
        return a.ConstructorArguments[0].Value as string;
    return null;
  }

  void AddIncludesOf(ISymbol s)
  {
    for (var x = s; x != null; x = x.ContainingType)
      foreach (var a in x.OriginalDefinition.GetAttributes())
        if (a.AttributeClass != null && a.AttributeClass.Name == "CppIncludeAttribute" && a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is string h)
          Includes.Add(h.StartsWith("<") ? h : (h.StartsWith("\"") ? h : "\"" + h + "\""));
  }

  public static BridgePlan Build(PartitionPlan plan)
  {
    var b = new BridgePlan(plan);
    var seen = new Dictionary<ISymbol, BridgeMethod>(SymbolEqualityComparer.Default);
    var reported = new HashSet<string>();
    var ctorCount = new Dictionary<ClassPlan, int>();
    string Who(CrossRef r) { return Side(r.From.Final) + " class `" + r.From.Name + "`"; }
    string Whom(CrossRef r) { return Side(r.To.Final) + " class `" + r.To.Name + "` (" + (r.To.Final == Part.Managed ? r.To.Reason : "lowered to Crust") + ")"; }
    void Err(CrossRef r, string msg)
    {
      string text = r.File + ":" + r.Line + ": " + msg;
      //a native class that uses a managed class in a way that cannot cross is not wrong: it follows the class it uses (unless it insists on [Native])
      if (r.From.Final == Part.Native && r.To.Final == Part.Managed && r.From.Explicit != Part.Native) {
        //(why, briefly: the other class's own reason for being managed is in its own line)
        if (!b.Demotable.ContainsKey(r.From)) b.Demotable[r.From] = r.File.Substring(r.File.LastIndexOfAny(new[] { '/', '\\' }) + 1) + ":" + r.Line + ": "
          + msg.Replace(" (" + r.To.Reason + ")", "").Replace("native class `" + r.From.Name + "` ", "");
        return;
      }
      if (reported.Add(text)) b.Errors.Add(text);
    }
    const string WhyStatic = "Native classes (other than arena classes) can be used from managed code through their static methods only; instances of an arena class, and managed instances held by native code, can be used.";

    //Uses of the other side's class that cannot be planned are ONE diagnostic for each pair of classes, which lists them:
    //one `Script s = new Script(); s.Go();` is four references, and one mistake.
    var refused = new Dictionary<(ClassPlan, ClassPlan), (CrossRef first, List<string> what, string why)>();
    void Refuse(CrossRef r, string what, string why)
    {
      var key = (r.From, r.To);
      if (!refused.TryGetValue(key, out var e)) { e = (r, new List<string>(), why); refused[key] = e; }
      if (!e.what.Contains(what)) e.what.Add(what);
    }
    //Native code holds an object of the managed class `r.To`.  False (and the reason recorded) if it cannot.
    bool HoldObject(CrossRef r)
    {
      string no = b.NotAnObject(r.To);
      if (no != null) { Refuse(r, "an object of it", "Native code cannot hold an object of it: " + no + "."); return false; }
      if (!b.Proxied.Contains(r.To)) b.Proxied.Add(r.To);
      return true;
    }

    foreach (var r in plan.Refs) {
      bool toManaged = r.From.Final == Part.Native && r.To.Final == Part.Managed;
      bool toArena = r.From.Final == Part.Managed && r.To.Final == Part.Native && ArenaClass.Is(r.To.Sym);
      string objWhy = WhyStatic;
      switch (r.Via) {
        case "type":
          if (r.QualifiesStaticCall) break;
          if (toManaged) HoldObject(r); else if (toArena) b.ProxyArena(r.To); else Refuse(r, "the type `" + r.What + "`", objWhy);
          break;
        case "typed":
          if (toManaged) HoldObject(r); else if (toArena) b.ProxyArena(r.To); else Refuse(r, "a `" + r.What + "` variable, field or parameter", objWhy);
          break;
        case "new": {
          var ctor = r.Member as IMethodSymbol;
          if (!(toManaged || toArena)) { Refuse(r, "`" + r.What + "`", objWhy); break; }
          if (ctor == null) break;
          if (toManaged && !HoldObject(r)) break;
          if (toArena) b.ProxyArena(r.To);
          if (seen.ContainsKey(ctor)) break;
          int k = ctorCount.TryGetValue(r.To, out var c0) ? c0 : 0;
          var bm = b.DescribeMethod(r, ctor, MKind.Ctor, k, ctor.ContainingType.Name, false, Err, Who, Whom);
          if (bm != null) { ctorCount[r.To] = k + 1; seen[ctor] = bm; b.Methods.Add(bm); }
          break;
        }
        case "member": {
          var sym = r.Member;
          if (sym is IFieldSymbol cf && cf.IsConst) {
            //a constant is folded by the compiler: native code never needs the other side for it, and managed code needs it declared
            if (r.From.Final == Part.Managed && r.To.Final == Part.Native && !b.Consts.Any(c => SymbolEqualityComparer.Default.Equals(c.Field, cf)))
              b.Consts.Add(new ConstField { Owner = r.To, Field = cf });
            break;
          }
          var m = sym as IMethodSymbol;
          if (m != null && m.IsStatic && m.MethodKind == MethodKind.Ordinary) {
            if (seen.ContainsKey(m)) break;
            var bm = b.DescribeMethod(r, m, MKind.Static, 0, m.Name, true, Err, Who, Whom);
            if (bm != null) { seen[m] = bm; b.Methods.Add(bm); }
          } else if (m != null && !m.IsStatic && m.MethodKind == MethodKind.Ordinary) {
            if (!(toManaged || toArena)) { Refuse(r, "the instance method `" + r.What + "`", objWhy); break; }
            if (toManaged && !HoldObject(r)) break;
            if (toArena) b.ProxyArena(r.To);
            if (seen.ContainsKey(m)) break;
            var bm = b.DescribeMethod(r, m, MKind.Instance, 0, m.Name, false, Err, Who, Whom);
            if (bm != null) { seen[m] = bm; b.Methods.Add(bm); }
          } else if (sym is IPropertySymbol prop && !prop.IsIndexer && (toArena || (toManaged && !prop.IsStatic))) {
            if (toManaged && !HoldObject(r)) break;
            if (toArena) b.ProxyArena(r.To);
            foreach (var acc in new[] { prop.GetMethod, prop.SetMethod }) {
              if (acc == null || seen.ContainsKey(acc)) continue;
              var bm = b.DescribeMethod(r, acc, acc == prop.GetMethod ? MKind.Getter : MKind.Setter, 0, prop.Name, prop.IsStatic, Err, Who, Whom);
              if (bm != null) { seen[acc] = bm; b.Methods.Add(bm); }
            }
          } else if (sym is IFieldSymbol fld && toArena) {
            b.ProxyArena(r.To);
            if (seen.ContainsKey(fld)) break;
            seen[fld] = null;                                  //(a field is a getter and, unless it is readonly, a setter: both, once)
            foreach (bool set in new[] { false, true }) {
              if (set && fld.IsReadOnly) continue;
              var bm = b.DescribeField(r, fld, set, Err, Who, Whom);
              if (bm != null) b.Methods.Add(bm);
            }
          } else if (sym is IFieldSymbol && !sym.IsStatic && toManaged) {
            Err(r, Who(r) + " uses the field `" + r.What + "` of " + Whom(r) + ": a field of a managed object cannot be reached through a handle. Make it a property (or add a method).");
          } else if (sym is IPropertySymbol { IsIndexer: true } && (toManaged || toArena)) {
            Err(r, Who(r) + " uses an indexer of " + Whom(r) + ": an indexer cannot cross the boundary. Call a method.");
          } else if (sym.IsStatic && !(sym is IMethodSymbol)) {
            Err(r, Who(r) + " uses the static " + (sym is IFieldSymbol ? "field" : "property") + " `" + r.What + "` of " + Whom(r)
              + ". Across the boundary only static methods can be called: make it a method.");
          } else {
            Refuse(r, "`" + r.What + "`", (toManaged || toArena) ? "That kind of member cannot cross the boundary." : objWhy);
          }
          break;
        }
      }
    }
    foreach (var kv in refused)
      Err(kv.Value.first, Who(kv.Value.first) + " uses " + Whom(kv.Value.first) + " as an object: " + string.Join(", ", kv.Value.what) + ". " + kv.Value.why);
    return b;
  }

  BridgeParam ParamOf(IParameterSymbol p, Dir dir, CrossRef r, string at, Action<CrossRef, string> err, Func<CrossRef, string> who, Func<CrossRef, string> whom, ref bool ok)
  {
    if (p.IsParams) { err(r, who(r) + " calls " + at + " of " + whom(r) + ": `params` cannot cross the boundary. Pass an array."); ok = false; return null; }
    var t = TypeOf(p.Type, dir, false, out string why);
    if (t == null || t.IsVoid) { err(r, who(r) + " calls " + at + " of " + whom(r) + ": parameter `" + p.Name + "`: " + (why ?? "void") + ". Allowed: " + AllowedTypes + "."); ok = false; return null; }
    if (p.RefKind != RefKind.None) {
      if (dir == Dir.NativeToManaged || !(t.Scalar && !t.IsBool)) {
        err(r, who(r) + " calls " + at + " of " + whom(r) + ": parameter `" + p.Name + "` is `" + p.RefKind.ToString().ToLowerInvariant()
          + "`, which can cross only as a number, and only to native code.");
        ok = false;
        return null;
      }
    }
    return new BridgeParam { Name = p.Name, Type = t, Ref = p.RefKind };
  }

  BridgeMethod DescribeMethod(CrossRef r, IMethodSymbol m, MKind kind, int ctorIndex, string name, bool isStatic,
                              Action<CrossRef, string> err, Func<CrossRef, string> who, Func<CrossRef, string> whom)
  {
    string at = "`" + r.What + "`";
    var owner = m.ContainingType;
    //(at a call site the method is the constructed `Id<int>`, whose TypeParameters are empty: ask whether it is generic at all)
    if (owner.IsGenericType || m.IsGenericMethod) { err(r, who(r) + " calls the generic " + at + " of " + whom(r) + ": generic classes and methods cannot cross the boundary."); return null; }
    if (kind == MKind.Static || kind == MKind.Instance) {
      if (owner.GetMembers(m.Name).OfType<IMethodSymbol>().Count(x => x.MethodKind == MethodKind.Ordinary) > 1) {
        err(r, who(r) + " calls " + at + " of " + whom(r) + ", which is overloaded: a method that crosses the boundary needs a name of its own.");
        return null;
      }
    }
    var dir = r.To.Final == Part.Managed ? Dir.NativeToManaged : Dir.ManagedToNative;
    var bm = new BridgeMethod {
      Owner = r.To, Symbol = m, Dir = dir, Kind = kind, IsStatic = isStatic, CtorIndex = ctorIndex,
      Flat = FlatOf(owner), Cls = owner.Name, Name = name, Access = Access(m),
      CppName = kind == MKind.Getter ? "get_" + name : kind == MKind.Setter ? "set_" + name : CrustEmitter.IdentOf(name),
      Ns = owner.ContainingNamespace == null || owner.ContainingNamespace.IsGlobalNamespace ? "" : owner.ContainingNamespace.ToDisplayString(),
      CsClass = Global(owner),
    };
    if (kind == MKind.Static && dir == Dir.ManagedToNative) { bm.Template = ExternTemplate(m); if (bm.Template != null) AddIncludesOf(m); }
    bool ok = true;
    foreach (var p in m.Parameters) {
      var bp = ParamOf(p, dir, r, at, err, who, whom, ref ok);
      if (bp != null) bm.Params.Add(bp);
    }
    if (kind == MKind.Ctor) {
      bm.Ret = dir == Dir.NativeToManaged ? new BType { Kind = 'o', IsObject = true, Cs = bm.CsClass, Cpp = bm.Flat, C = "long long" }
                                          : new BType { Kind = 'a', IsArena = true, Cs = bm.CsClass, Cpp = bm.Flat, C = "long long", Manifest = "long" };
    } else {
      var rt = TypeOf(m.ReturnType, dir, true, out string rwhy);
      if (rt == null) { err(r, who(r) + " calls " + at + " of " + whom(r) + ": the result: " + rwhy + ". Allowed: " + AllowedTypes + "."); ok = false; }
      else bm.Ret = rt;
    }
    if (!ok) return null;
    return Finish(r, bm, at, err, who, whom);
  }

  /** An instance or static field of a native arena class, read or written from managed code. */
  BridgeMethod DescribeField(CrossRef r, IFieldSymbol f, bool set, Action<CrossRef, string> err, Func<CrossRef, string> who, Func<CrossRef, string> whom)
  {
    string at = "`" + r.What + "`";
    var owner = f.ContainingType;
    var t = TypeOf(f.Type, Dir.ManagedToNative, false, out string why);
    if (t == null || t.IsVoid) { err(r, who(r) + " uses the field " + at + " of " + whom(r) + ": " + (why ?? "void") + ". Allowed: " + AllowedTypes + "."); return null; }
    var bm = new BridgeMethod {
      Owner = r.To, Symbol = f, Dir = Dir.ManagedToNative, Kind = set ? MKind.FieldSet : MKind.FieldGet, IsStatic = f.IsStatic,
      Flat = FlatOf(owner), Cls = owner.Name, Name = f.Name, Access = Access(f), CppName = CrustEmitter.IdentOf(f.Name),
      Ns = owner.ContainingNamespace == null || owner.ContainingNamespace.IsGlobalNamespace ? "" : owner.ContainingNamespace.ToDisplayString(),
      CsClass = Global(owner),
    };
    if (set) { bm.Params.Add(new BridgeParam { Name = "value", Type = t }); bm.Ret = new BType { Kind = 'v', IsVoid = true, Cs = "void", Cpp = "void", C = "void" }; }
    else bm.Ret = t;
    return Finish(r, bm, at, err, who, whom);
  }

  BridgeMethod Finish(CrossRef r, BridgeMethod bm, string at, Action<CrossRef, string> err, Func<CrossRef, string> who, Func<CrossRef, string> whom)
  {
    if (bm.Dir == Dir.ManagedToNative) {
      if (bm.Ret.IsArray) { err(r, who(r) + " calls " + at + " of " + whom(r) + ", which returns an array: returning an array from native to managed is not supported yet. Return a string, or fill an array parameter."); return null; }
    }
    if (bm.Dir == Dir.NativeToManaged && bm.FfiArgs > 16) {
      err(r, who(r) + " calls " + at + " of " + whom(r) + ": it takes " + bm.FfiArgs + " C arguments (an array is two, an object is one more), and a call from C to managed code takes at most 16.");
      return null;
    }
    if (bm.Params.Any(p => p.Type.IsString) || bm.Ret.IsString) UsesString = true;
    return bm;
  }

  // ------------------------------------------------------------------ C++: proxies for managed classes, exports of native ones

  static string A(int i) { return "a" + i; }

  /** The C prototype of a native -> managed bridge function (defined in the glue). */
  static string ProxyCDecl(BridgeMethod m)
  {
    var ps = new List<string>();
    if (m.HasSelf) ps.Add("long long h");
    for (int i = 0; i < m.Params.Count; i++) {
      var t = m.Params[i].Type;
      if (t.IsArray) { ps.Add(t.C + " * " + A(i)); ps.Add("int " + A(i) + "_len"); }
      else ps.Add(t.C + " " + A(i));
    }
    string ret = m.Ret.IsArray ? m.Ret.C + " *" : m.Ret.C;
    if (m.Ret.IsArray) ps.Add("int * ccs_outlen");
    return ret + " " + m.CName + "(" + (ps.Count == 0 ? "void" : string.Join(", ", ps)) + ");";
  }

  /** Proxy classes: what the native code calls where it calls a managed method.  The call sites are the ordinary `Script::Go(..)` / `s.Go(..)`. */
  public string CppProxies()
  {
    var sb = new StringBuilder();
    foreach (var m in ToManaged) sb.Append(ProxyCDecl(m)).Append('\n');
    if (Proxied.Count > 0) sb.Append("void ccs_b_release(long long h);\n");
    var flats = ToManaged.Select(m => m.Flat).Concat(Proxied.Select(p => FlatOf(p.Sym))).Distinct().ToList();
    foreach (var flat in flats) {
      var members = ToManaged.Where(m => m.Flat == flat).ToList();
      bool obj = Proxied.Any(p => FlatOf(p.Sym) == flat);
      sb.Append("class ").Append(flat).Append(" {\n");
      if (obj) sb.Append("long long _ccs_h = 0;\n");
      if (obj && members.Any(m => m.Kind == MKind.Ctor)) sb.Append('~').Append(flat).Append("() { ccs_b_release(_ccs_h); }\n");
      foreach (var m in members) {
        var ps = new List<string>();
        var args = new List<string>();
        if (m.HasSelf) args.Add("_ccs_h");
        for (int i = 0; i < m.Params.Count; i++) {
          var t = m.Params[i].Type;
          if (t.IsArray) {
            ps.Add("std::vector<" + t.Cpp + "> & " + A(i));
            args.Add(A(i) + ".size() == 0 ? 0 : &" + A(i) + "[0]");
            args.Add(A(i) + ".size()");
          } else if (t.IsString) { ps.Add("fastring " + A(i)); args.Add(A(i) + ".c_str()"); }
          else if (t.IsObject) { ps.Add(t.Cpp + " & " + A(i)); args.Add(A(i) + "._ccs_h"); }
          else if (t.IsArena) { ps.Add(t.Cpp + " * " + A(i)); args.Add("(long long)" + A(i)); }
          else if (t.IsBool) { ps.Add("bool " + A(i)); args.Add("(" + A(i) + " ? 1 : 0)"); }
          else { ps.Add(t.Cpp + " " + A(i)); args.Add(A(i)); }
        }
        string call = m.CName + "(" + string.Join(", ", args) + ")";
        if (m.Kind == MKind.Ctor) { sb.Append(flat).Append('(').Append(string.Join(", ", ps)).Append(") { _ccs_h = ").Append(call).Append("; }\n"); continue; }
        string rt = m.Ret.IsArray ? "std::vector<" + m.Ret.Cpp + ">" : m.Ret.IsArena ? m.Ret.Cpp + " *" : m.Ret.Cpp;
        sb.Append(m.Kind == MKind.Static ? "static " : "").Append(rt).Append(' ').Append(m.CppName).Append('(').Append(string.Join(", ", ps)).Append(") { ");
        if (m.Ret.IsVoid) sb.Append(call).Append("; ");
        else if (m.Ret.IsString) sb.Append("fastring _cb_r; _cb_r.append_cstr(").Append(call).Append("); return _cb_r; ");
        else if (m.Ret.IsArena) sb.Append("return (").Append(m.Ret.Cpp).Append(" *)").Append(call).Append("; ");
        else if (m.Ret.IsArray) {
          args.Add("&_cb_n");
          sb.Append("int _cb_n = 0; ").Append(m.Ret.C).Append(" * _cb_p = ").Append(m.CName).Append('(').Append(string.Join(", ", args)).Append("); ")
            .Append("std::vector<").Append(m.Ret.Cpp).Append("> _cb_v; for (int _cb_i = 0; _cb_i < _cb_n; _cb_i++) { _cb_v.push_back(_cb_p[_cb_i]); } return _cb_v; ");
        }
        else if (m.Ret.IsBool) sb.Append("return ").Append(call).Append(" != 0; ");
        else sb.Append("return ").Append(call).Append("; ");
        sb.Append("}\n");
      }
      sb.Append("};\n");
    }
    return sb.ToString();
  }

  /** The functions managed code reaches through [DllImport], defined after the native classes they use. */
  public string CppExports()
  {
    var sb = new StringBuilder();
    foreach (var m in ToNative) {
      var ps = new List<string>();
      if (m.HasSelf) ps.Add("long long h");
      for (int i = 0; i < m.Params.Count; i++) {
        var p = m.Params[i]; var t = p.Type;
        if (t.IsArray) { ps.Add(t.C + " * " + A(i)); ps.Add("int " + A(i) + "_len"); }
        else if (p.Ref != RefKind.None) ps.Add(t.C + " * " + A(i));
        else ps.Add(t.C + " " + A(i));
      }
      string rt = m.Ret.IsString ? "char *" : m.Ret.C;
      sb.Append(rt).Append(' ').Append(m.CName).Append('(').Append(string.Join(", ", ps)).Append(") {\n");
      if (m.HasSelf) sb.Append(m.Flat).Append(" * _cb_self = (").Append(m.Flat).Append(" *)h;\n");
      var args = new List<string>();
      var back = new StringBuilder();
      for (int i = 0; i < m.Params.Count; i++) {
        var p = m.Params[i]; var t = p.Type;
        if (t.IsString) { sb.Append("fastring _cb_s").Append(i).Append("; _cb_s").Append(i).Append(".append_cstr(").Append(A(i)).Append(");\n"); args.Add("_cb_s" + i); }
        else if (t.IsArray) {
          sb.Append("std::vector<").Append(t.Cpp).Append("> _cb_v").Append(i).Append("; for (int _cb_i = 0; _cb_i < ").Append(A(i)).Append("_len; _cb_i++) { _cb_v").Append(i)
            .Append(".push_back(").Append(A(i)).Append("[_cb_i]); }\n");
          args.Add("_cb_v" + i);
          back.Append("for (int _cb_i = 0; _cb_i < ").Append(A(i)).Append("_len; _cb_i++) { ").Append(A(i)).Append("[_cb_i] = _cb_v").Append(i).Append("[_cb_i]; }\n");
        }
        else if (t.IsArena) { sb.Append(t.Cpp).Append(" * _cb_a").Append(i).Append(" = (").Append(t.Cpp).Append(" *)").Append(A(i)).Append(";\n"); args.Add("_cb_a" + i); }
        else if (p.Ref != RefKind.None) {
          sb.Append(t.C).Append(" _cb_o").Append(i).Append(" = ").Append(p.Ref == RefKind.Out ? "0" : "*" + A(i)).Append(";\n");
          args.Add("_cb_o" + i);
          back.Append("*").Append(A(i)).Append(" = _cb_o").Append(i).Append(";\n");
        }
        else if (t.IsBool) args.Add("(" + A(i) + " != 0)");
        else args.Add(A(i));
      }
      string recv = m.HasSelf ? "_cb_self->" : m.Flat + "::";
      string call;
      switch (m.Kind) {
        case MKind.Ctor: call = "new " + m.Flat + "(" + string.Join(", ", args) + ")"; break;
        case MKind.FieldGet: call = (m.HasSelf ? "_cb_self->" : m.Flat + "::") + m.CppName; break;
        case MKind.FieldSet: call = (m.HasSelf ? "_cb_self->" : m.Flat + "::") + m.CppName + " = " + args[0]; break;
        case MKind.Getter: call = recv + "get_" + m.CppName.Substring(4) + "()"; break;
        case MKind.Setter: call = recv + "set_" + m.CppName.Substring(4) + "(" + string.Join(", ", args) + ")"; break;
        default:
          if (m.Template != null) {
            //a foreign C function: a call spells the template, not `Class::Name(..)`
            call = System.Text.RegularExpressions.Regex.Replace(m.Template, @"\{(\d+)(:c)?\}", mm => {
              int k = int.Parse(mm.Groups[1].Value);
              return mm.Groups[2].Success && m.Params[k].Type.IsString ? A(k) : args[k];
            });
          } else call = recv + m.CppName + "(" + string.Join(", ", args) + ")";
          break;
      }
      if (m.Ret.IsVoid) sb.Append(call).Append(";\n").Append(back);
      else if (m.Kind == MKind.Ctor) sb.Append(m.Ret.Cpp).Append(" * _cb_r = ").Append(call).Append(";\nreturn (long long)_cb_r;\n");
      else if (m.Ret.IsString) sb.Append("fastring _cb_r = ").Append(call).Append(";\n").Append(back).Append("char * _cb_o = strdup(_cb_r.c_str());\nreturn _cb_o;\n");
      else if (m.Ret.IsArena) sb.Append(m.Ret.Cpp).Append(" * _cb_r = ").Append(call).Append(";\n").Append(back).Append("return (long long)_cb_r;\n");
      else sb.Append(m.Ret.Cpp).Append(" _cb_r = ").Append(call).Append(";\n").Append(back).Append(m.Ret.IsBool ? "return _cb_r ? 1 : 0;\n" : "return _cb_r;\n");
      sb.Append("}\n");
    }
    return sb.ToString();
  }

  // ------------------------------------------------------------------ C: the glue that calls DotNetAnywhere

  static string DnaValue(BType t, string v, string len)
  {
    switch (t.Kind) {
      case 'i': return "DNA_Int(" + v + ")";
      case 'l': case 'a': return "DNA_Long(" + v + ")";
      case 'f': return "DNA_Float(" + v + ")";
      case 'd': return "DNA_Double(" + v + ")";
      case 's': return "DNA_Str(" + v + ")";
      case 'o': return "DNA_Obj(" + v + ")";
      default: return "DNA_Array(" + v + ", " + len + ", " + t.Elem + ")";
    }
  }

  public string GlueC(string target, bool managedEntry)
  {
    var sb = new StringBuilder();
    sb.Append("/* CC# --dna: the C side of the native/managed bridge.  Generated. */\n"
      + "#include <stdio.h>\n#include <stdlib.h>\n#include <string.h>\n#include <unistd.h>\n#include \"Host.h\"\n\n"
      + "static DNA_Assembly *ccs_asm;\n\n"
      + "static const char *ccs_dll_path(char *buf, size_t n) {\n"
      + "\tconst char *e = getenv(\"CCS_MANAGED_DLL\");\n"
      + "\tssize_t k;\n"
      + "\tif (e != NULL && *e) return e;\n"
      + "\tk = readlink(\"/proc/self/exe\", buf, n - 1);\n"
      + "\tif (k > 0) {\n"
      + "\t\tchar *s;\n"
      + "\t\tbuf[k] = 0;\n"
      + "\t\ts = strrchr(buf, '/');\n"
      + "\t\tif (s != NULL) {\n"
      + "\t\t\tsnprintf(s + 1, n - (size_t)(s + 1 - buf), \"%s\", \"" + target + ".managed.dll\");\n"
      + "\t\t\tif (access(buf, R_OK) == 0) return buf;\n"
      + "\t\t}\n"
      + "\t}\n"
      + "\treturn \"" + target + ".managed.dll\";\n"
      + "}\n\n"
      + "/* CCS_CHECK_HANDLES=1: a program that ends with managed objects still held by native code (a handle never released) says so, and exits 71 */\n"
      + "static void ccs_leak_check(void) {\n"
      + "\tconst char *e = getenv(\"CCS_CHECK_HANDLES\");\n"
      + "\tif (e != NULL && *e == '1' && DNA_LiveHandles() != 0) {\n"
      + "\t\tfprintf(stderr, \"cc#: %d managed object handle(s) were never released\\n\", DNA_LiveHandles());\n"
      + "\t\t_exit(71);\n"
      + "\t}\n"
      + "}\n\n"
      + "static void ccs_ensure(void) {\n"
      + "\tchar buf[4096];\n"
      + "\tconst char *path;\n"
      + "\tif (ccs_asm != NULL) return;\n"
      + "\tpath = ccs_dll_path(buf, sizeof buf);\n"
      + "\tDNA_SetCrashMode(1);                  /* an uncaught exception ends the process as it does in .NET and in Crust */\n"
      + "\tDNA_SetAssemblyDirFromFile(path);      /* corlib.dll is beside the managed assembly, wherever the program is started */\n"
      + "\tDNA_Init();\n"
      + "\tatexit(ccs_leak_check);\n"
      + "\tccs_asm = DNA_Load(path);\n"
      + "\tif (ccs_asm == NULL) { fprintf(stderr, \"cc#: cannot load the managed assembly: %s\\n\", DNA_Error()); exit(70); }\n"
      + "}\n\n"
      + "static DNA_Method *ccs_find(const char *ns, const char *cls, const char *name, const char *sig) {\n"
      + "\tDNA_Method *m;\n"
      + "\tccs_ensure();\n"
      + "\tm = DNA_Find(ccs_asm, ns, cls, name, sig);\n"
      + "\tif (m == NULL) { fprintf(stderr, \"cc#: %s\\n\", DNA_Error()); exit(70); }\n"
      + "\treturn m;\n"
      + "}\n\n"
      + "static void ccs_call(DNA_Method *m, const DNA_Value *a, int n, DNA_Value *r) {\n"
      + "\tif (DNA_Call(m, a, n, r) != 0) { fprintf(stderr, \"cc#: %s\\n\", DNA_Error()); exit(70); }\n"
      + "}\n\n");
    if (Proxied.Count > 0)
      sb.Append("void ccs_b_release(long long h) {\n\tif (ccs_asm != NULL && DNA_Release((int64_t)h) != 0) { fprintf(stderr, \"cc#: %s\\n\", DNA_Error()); exit(70); }\n}\n\n");
    foreach (var m in ToManaged) {
      var ps = new List<string>();
      if (m.HasSelf) ps.Add("long long h");
      for (int i = 0; i < m.Params.Count; i++) {
        var t = m.Params[i].Type;
        if (t.IsArray) { ps.Add(t.C + " * " + A(i)); ps.Add("int " + A(i) + "_len"); }
        else ps.Add(t.C + " " + A(i));
      }
      if (m.Ret.IsArray) ps.Add("int * ccs_outlen");
      string rt = m.Ret.IsArray ? m.Ret.C + " *" : m.Ret.C;
      int n = m.Params.Count + (m.HasSelf ? 1 : 0);
      sb.Append(rt).Append(' ').Append(m.CName).Append('(').Append(ps.Count == 0 ? "void" : string.Join(", ", ps)).Append(") {\n");
      sb.Append("\tstatic DNA_Method *m;\n\tDNA_Value a[").Append(Math.Max(1, n)).Append("], r;\n");
      sb.Append("\tif (m == NULL) m = ccs_find(\"").Append(m.FindNs).Append("\", \"").Append(m.FindClass).Append("\", \"").Append(m.FindName).Append("\", \"").Append(m.Sig).Append("\");\n");
      int at = 0;
      if (m.HasSelf) sb.Append("\ta[").Append(at++).Append("] = DNA_Obj(h);\n");
      for (int i = 0; i < m.Params.Count; i++)
        sb.Append("\ta[").Append(at++).Append("] = ").Append(DnaValue(m.Params[i].Type, A(i), A(i) + "_len")).Append(";\n");
      sb.Append("\tccs_call(m, a, ").Append(n).Append(", &r);\n");
      switch (m.Ret.Kind) {
        case 'v': break;
        case 'i': sb.Append("\treturn (").Append(m.Ret.C).Append(")r.u.i;\n"); break;
        case 'l': sb.Append("\treturn (").Append(m.Ret.C).Append(")r.u.l;\n"); break;
        case 'o': case 'a': sb.Append("\treturn (long long)r.u.l;\n"); break;
        case 'f': sb.Append("\treturn r.u.f;\n"); break;
        case 'd': sb.Append("\treturn r.u.d;\n"); break;
        case 's': sb.Append("\treturn r.u.s != NULL ? r.u.s : \"\";\n"); break;
        default: sb.Append("\t*ccs_outlen = r.len;\n\treturn (").Append(m.Ret.C).Append(" *)r.u.data;\n"); break;
      }
      sb.Append("}\n\n");
    }
    if (managedEntry)
      sb.Append("int main(int argc, char **argv) {\n\tccs_ensure();\n\treturn DNA_RunMain(ccs_asm, argc, argv);\n}\n");
    return sb.ToString();
  }

  // ------------------------------------------------------------------ the managed side

  static string ConstLiteral(IFieldSymbol f, out string type)
  {
    var v = f.ConstantValue;
    type = f.Type.TypeKind == TypeKind.Enum ? Global((INamedTypeSymbol)f.Type) : f.Type.ToDisplayString();
    if (v == null) return "null";
    string lit = SymbolDisplay.FormatPrimitive(v, true, false);
    if (f.Type.TypeKind == TypeKind.Enum) return "(" + type + ")" + lit;
    return lit;
  }

  /** The managed side's view of a conversion at the boundary. */
  static string ToExtern(BridgeParam p, string n)
  {
    if (p.Type.IsArray) return n + ", " + n + ".Length";
    if (p.Type.IsArena) return p.Type.Cs + ".Ptr(" + n + ")";
    if (p.Ref != RefKind.None) return "ref " + n;
    if (p.Type.IsBool) return "(" + n + " ? 1 : 0)";
    return n;
  }

  string FromExtern(BType t, string call)
  {
    if (t.IsBool) return call + " != 0";
    if (t.IsArena) return t.Cs + ".Wrap(" + call + ")";
    return call;
  }

  /** The C# the managed side compiles in addition to its own classes: the trampolines that DNA calls to reach instances (and to pass native objects), and
      the stand-ins for the native classes that managed code uses. */
  public string CsProxies()
  {
    var sb = new StringBuilder();
    sb.Append("// CC# --dna: what the managed side needs from the bridge.  Generated.\n");

    //DNA's host API calls only static methods with numbers, strings, arrays and handles: an instance member is one, and so is a call that carries a native object
    var tramps = ToManaged.Where(m => m.NeedsTrampoline).ToList();
    if (tramps.Count > 0) {
      sb.Append("internal static class __ccs_bridge {\n");
      foreach (var m in tramps) {
        var ps = new List<string>();
        var args = new List<string>();
        if (m.HasSelf) ps.Add(m.CsClass + " self");
        for (int i = 0; i < m.Params.Count; i++) {
          var t = m.Params[i].Type;
          if (t.IsArena) { ps.Add("long " + A(i)); args.Add(t.Cs + ".Wrap(" + A(i) + ")"); }
          else { ps.Add(t.Cs + " " + A(i)); args.Add(A(i)); }
        }
        string name = m.Flat + "_" + m.Member;
        string call;
        switch (m.Kind) {
          case MKind.Ctor: call = "new " + m.CsClass + "(" + string.Join(", ", args) + ")"; break;
          case MKind.Getter: call = "self." + m.Name; break;
          case MKind.Setter: call = "self." + m.Name + " = " + args[0]; break;
          case MKind.Static: call = m.CsClass + "." + m.Name + "(" + string.Join(", ", args) + ")"; break;
          default: call = "self." + m.Name + "(" + string.Join(", ", args) + ")"; break;
        }
        string ret = m.Kind == MKind.Ctor ? m.CsClass : m.Ret.IsArena ? "long" : m.Ret.Cs;
        if (m.Ret.IsArena) call = m.Ret.Cs + ".Ptr(" + call + ")";
        sb.Append("  internal static ").Append(ret).Append(' ').Append(name).Append('(').Append(string.Join(", ", ps)).Append(") { ");
        sb.Append(m.Ret.IsVoid && m.Kind != MKind.Ctor ? call + "; " : "return " + call + "; ").Append("}\n");
      }
      sb.Append("}\n");
    }

    //the native classes that managed code uses: one proxy class for each
    var classes = ToNative.Select(m => m.Owner).Concat(Consts.Select(c => c.Owner)).Concat(ProxiedArena).Distinct().ToList();
    foreach (var cp in classes) {
      var sym = cp.Sym;
      bool arena = ArenaClass.Is(sym);
      var members = ToNative.Where(m => m.Owner == cp).ToList();
      string ns = sym.ContainingNamespace == null || sym.ContainingNamespace.IsGlobalNamespace ? "" : sym.ContainingNamespace.ToDisplayString();
      string cls = sym.Name;
      if (ns != "") sb.Append("namespace ").Append(ns).Append(" {\n");
      sb.Append(Access(sym)).Append(arena ? " sealed class " : " static class ").Append(cls).Append(" {\n");
      if (arena) {
        sb.Append("  internal long _p;\n");
        sb.Append("  private static readonly System.Collections.Generic.Dictionary<long, ").Append(cls).Append("> _all = new System.Collections.Generic.Dictionary<long, ").Append(cls).Append(">();\n");
        sb.Append("  private ").Append(cls).Append("(long p, bool wrap) { _p = p; _all[p] = this; }\n");
        sb.Append("  internal static ").Append(cls).Append(" Wrap(long p) { if (p == 0) return null; ").Append(cls).Append(" n; if (_all.TryGetValue(p, out n)) return n; return new ").Append(cls).Append("(p, true); }\n");
        sb.Append("  internal static long Ptr(").Append(cls).Append(" n) { return n == null ? 0 : n._p; }\n");
      }
      foreach (var c in Consts.Where(c => c.Owner == cp)) {
        string lit = ConstLiteral(c.Field, out string ty);
        sb.Append("  ").Append(Access(c.Field)).Append(" const ").Append(ty).Append(' ').Append(c.Field.Name).Append(" = ").Append(lit).Append(";\n");
      }
      //properties and fields are accessors with the same name: C# source reads and writes them the same way
      foreach (var g in members.Where(m => m.Kind == MKind.Getter || m.Kind == MKind.Setter || m.Kind == MKind.FieldGet || m.Kind == MKind.FieldSet).GroupBy(m => (m.Name, m.IsStatic))) {
        var get = g.FirstOrDefault(m => m.Kind == MKind.Getter || m.Kind == MKind.FieldGet);
        var set = g.FirstOrDefault(m => m.Kind == MKind.Setter || m.Kind == MKind.FieldSet);
        var any = get ?? set;
        string type = get != null ? get.Ret.Cs : set.Params[0].Type.Cs;
        foreach (var m in new[] { get, set }) if (m != null) AppendExtern(sb, m);
        sb.Append("  ").Append(any.Access).Append(any.IsStatic ? " static " : " ").Append(type).Append(' ').Append(any.Name).Append(" {");
        if (get != null) sb.Append(" get { return ").Append(FromExtern(get.Ret, get.CName + "(" + (get.HasSelf ? "_p" : "") + ")")).Append("; }");
        if (set != null) sb.Append(" set { ").Append(set.CName).Append("(").Append(set.HasSelf ? "_p, " : "").Append(ToExtern(set.Params[0], "value")).Append("); }");
        sb.Append(" }\n");
      }
      foreach (var m in members.Where(m => m.Kind == MKind.Static || m.Kind == MKind.Instance || m.Kind == MKind.Ctor)) {
        AppendExtern(sb, m);
        var ps = new List<string>();
        var args = new List<string>();
        if (m.HasSelf) args.Add("_p");
        var pre = new StringBuilder();
        var post = new StringBuilder();
        for (int i = 0; i < m.Params.Count; i++) {
          var p = m.Params[i]; string n = "@" + p.Name;
          string mod = p.Ref == RefKind.Out ? "out " : p.Ref == RefKind.Ref ? "ref " : "";
          ps.Add(mod + p.Type.Cs + " " + n);
          if (p.Ref == RefKind.Out) pre.Append(n).Append(" = default(").Append(p.Type.Cs).Append("); ");
          args.Add(ToExtern(p, n));
        }
        string call = m.CName + "(" + string.Join(", ", args) + ")";
        if (m.Kind == MKind.Ctor) {
          sb.Append("  ").Append(m.Access).Append(' ').Append(cls).Append('(').Append(string.Join(", ", ps)).Append(") : this(").Append(call).Append(", true) { }\n");
          continue;
        }
        string stat = m.Kind == MKind.Static ? "static " : "";
        sb.Append("  ").Append(m.Access).Append(' ').Append(stat).Append(m.Ret.Cs).Append(' ').Append(m.Name).Append('(').Append(string.Join(", ", ps)).Append(") { ").Append(pre);
        if (m.Ret.IsVoid) sb.Append(call).Append("; ");
        else sb.Append("return ").Append(FromExtern(m.Ret, call)).Append("; ");
        sb.Append("}\n");
      }
      sb.Append("}\n");
      if (ns != "") sb.Append("}\n");
    }
    return sb.ToString();
  }

  static void AppendExtern(StringBuilder sb, BridgeMethod m)
  {
    var ext = new List<string>();
    if (m.HasSelf) ext.Add("long h");
    for (int i = 0; i < m.Params.Count; i++) {
      var p = m.Params[i]; var t = p.Type;
      if (t.IsArray) { ext.Add(t.Cs + " " + A(i)); ext.Add("int " + A(i) + "_len"); }
      else if (t.IsArena) ext.Add("long " + A(i));
      else if (p.Ref != RefKind.None) ext.Add("ref " + t.Cs + " " + A(i));
      else if (t.IsBool) ext.Add("int " + A(i));
      else ext.Add(t.Cs + " " + A(i));
    }
    string ret = m.Kind == MKind.Ctor || m.Ret.IsArena ? "long" : m.Ret.IsBool ? "int" : m.Ret.Cs;
    sb.Append("  [System.Runtime.InteropServices.DllImport(\"ccs_native\")] private static extern ").Append(ret).Append(' ').Append(m.CName).Append('(').Append(string.Join(", ", ext)).Append(");\n");
  }

  public bool NeedsManagedExtras { get { return ToNative.Any() || Consts.Any() || ProxiedArena.Any() || ToManaged.Any(m => m.NeedsTrampoline); } }

  /** DotNetAnywhere's `build.py --ffi`: the native functions managed code may [DllImport].  They are linked from the program's own object, so no C files. */
  public string FfiManifest()
  {
    var fns = ToNative.Select(m => new Dictionary<string, object> {
      { "library", "ccs_native" }, { "entry", m.CName },
      { "ret", m.Ret.IsVoid ? "void" : m.Kind == MKind.Ctor || m.Ret.IsArena ? "long" : m.Ret.Manifest },
      { "args", (m.HasSelf ? new[] { "long" } : new string[0]).Concat(m.Params.SelectMany(p =>
          p.Type.IsArray ? new[] { "buf:" + p.Type.C + "*", "int" } :
          p.Ref != RefKind.None ? new[] { "ref:" + p.Type.C + "*" } :
          new[] { p.Type.Manifest })).ToList() },
    }).ToList();
    var o = new Dictionary<string, object> { { "c_files", new List<string>() }, { "cflags", new List<string>() }, { "functions", fns } };
    return JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true });
  }
}
