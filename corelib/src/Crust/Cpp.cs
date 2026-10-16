/*
  The mapping from the C# corelib to C++ (the subset Crust lowers) and coost.

  A member with no body is declared here and implemented in C++.  [Cpp] says how a use of it is spelled:

      [Cpp("{this}.substr({0})")]          instance method / property getter
      [Cpp("cs_abs_i32({0})")]             static method
      [Cpp("std::vector<{T0}>")]           on a type: how the type is spelled; {T0} {T1} are its type arguments

  Placeholders:  {this}  the receiver          {0} {1} ..  the arguments
                 {0:c}   argument 0 as a `const char *` (a literal as written, a string's c_str())

  A string-typed receiver or argument is always a *named* fastring (Crust passes references as pointers, so
  a temporary has no address); the emitter builds it into a local first.

  A member declared without [Cpp] is part of the .NET surface that is not implemented in Crust yet: using it
  is refused, at the C# line, naming the member.  That is the contract of this corelib, and it is why
  everything .NET has is not simply deleted from here: the C# compiler can then say "not implemented" rather
  than "no such member".
*/
namespace Crust {

  /** How a member (or a whole type) is written in C++. */
  [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = false)]
  public sealed class CppAttribute : System.Attribute {
    public CppAttribute(string template) {}
    /** C++ the template needs defined once, ahead of the code that uses it (a function that does what no single expression can). */
    public string Helper { get; set; }
  }

  /** How assigning to a corelib property is written in C++: `{this}` is the receiver, `{0}` the value.  A property without one cannot be assigned. */
  [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false)]
  public sealed class CppSetAttribute : System.Attribute {
    public CppSetAttribute(string template) {}
  }

  /** A header the generated code needs: "\"cs/core.h\"" or "<math.h>".  coost sources are spliced in by header. */
  [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true)]
  public sealed class CppIncludeAttribute : System.Attribute {
    public CppIncludeAttribute(string header) {}
  }

  /** The method returns its receiver (StringBuilder.Append): a chain `sb.Append(a).Append(b)` is one statement per call. */
  [System.AttributeUsage(System.AttributeTargets.Method)]
  public sealed class CppFluentAttribute : System.Attribute {
  }

  /** An arena class: `[MaxInstances(N)] class T`.  C#'s reference semantics without a GC: the class has N statically allocated slots, a reference
      is a plain pointer (assignment copies it, null is 0, == compares references), and `new T(..)` takes the next slot.  The (N+1)th live object
      aborts.  The attribute is found by name, so a program may declare its own marker class instead. */
  [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false)]
  public sealed class MaxInstancesAttribute : System.Attribute {
    public MaxInstancesAttribute(int n) {}
  }

  /** (--dna) The class runs on the managed side: Crust does not lower it, and DotNetAnywhere interprets its CIL.
      It overrides the default, which is to try Crust first and fall back to managed when the class is refused. */
  [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface, AllowMultiple = false)]
  public sealed class ManagedAttribute : System.Attribute {
  }

  /** (--dna) The class must be lowered to Crust C++.  If it is outside the Crust subset that is an error, as without --dna,
      and never a silent fall back to managed.  It overrides the default. */
  [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface, AllowMultiple = false)]
  public sealed class NativeAttribute : System.Attribute {
  }
}
