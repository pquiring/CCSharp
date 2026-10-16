/*
  The core types the C# language itself needs.  This corelib is compiled by Roslyn INSTEAD of the .NET
  reference assemblies, so it is also the whole of what a program can name.

  What was kept from the old (GC) corelib is the shape: the same namespaces, the same names.  What is gone is
  everything that assumed a garbage collector: System.Object as a universal root that every class derives
  from, UTF-16 `char` strings, reflection (Type, Class, Method), Thread and Mutex.
*/
namespace System {

  public class Object {
    public Object() {}
    public extern Type GetType();                                   // no reflection in Crust
    public virtual extern string ToString();
    public virtual extern bool Equals(object other);
    public virtual extern int GetHashCode();
  }

  public abstract class ValueType {}
  public abstract class Enum : ValueType {}
  public struct Void {}
  public struct Nullable<T> where T : struct {}                       // `T?` is refused: use a bool beside the value

  public sealed class Type {}

  public abstract class Delegate {}
  public abstract class MulticastDelegate : Delegate {}

  public abstract class Array {
    public extern int Length { get; }
    public extern int Rank { get; }
  }

  public interface IDisposable { void Dispose(); }
  public delegate int Comparison<in T>(T x, T y);
  public interface IComparable { int CompareTo(object obj); }
  public interface IComparable<in T> { int CompareTo(T other); }
  public interface IEquatable<T> { bool Equals(T other); }

  public delegate void Action();
  public delegate void Action<in T>(T arg);
  public delegate TResult Func<out TResult>();
  public delegate TResult Func<in T, out TResult>(T arg);

  // ---- attributes -------------------------------------------------------------------------------

  public abstract class Attribute {}

  [Flags]
  public enum AttributeTargets {
    Assembly = 1, Module = 2, Class = 4, Struct = 8, Enum = 16, Constructor = 32, Method = 64, Property = 128,
    Field = 256, Event = 512, Interface = 1024, Parameter = 2048, Delegate = 4096, ReturnValue = 8192,
    GenericParameter = 16384, All = 32767
  }

  [AttributeUsage(AttributeTargets.Class)]
  public sealed class AttributeUsageAttribute : Attribute {
    public AttributeUsageAttribute(AttributeTargets validOn) {}
    public bool AllowMultiple { get; set; }
    public bool Inherited { get; set; }
  }

  [AttributeUsage(AttributeTargets.Enum)]
  public sealed class FlagsAttribute : Attribute {}

  [AttributeUsage(AttributeTargets.Parameter)]
  public sealed class ParamArrayAttribute : Attribute {}

  public sealed class ObsoleteAttribute : Attribute {
    public ObsoleteAttribute() {}
    public ObsoleteAttribute(string message) {}
  }

  // ---- exceptions: declared so that user code binds; `throw` / `try` are refused by the emitter ----------

  public class Exception {
    public Exception() {}
    public Exception(string message) {}
    public extern string Message { get; }
  }
  public class SystemException : Exception { public SystemException() {} public SystemException(string message) {} }
  public class ArgumentException : SystemException { public ArgumentException() {} public ArgumentException(string message) {} public ArgumentException(string message, string paramName) {} }
  public class ArgumentNullException : ArgumentException { public ArgumentNullException() {} public ArgumentNullException(string message) {} public ArgumentNullException(string paramName, string message) {} }
  public class ArgumentOutOfRangeException : ArgumentException { public ArgumentOutOfRangeException() {} public ArgumentOutOfRangeException(string message) {} public ArgumentOutOfRangeException(string paramName, string message) {} }
  public class InvalidOperationException : SystemException { public InvalidOperationException() {} public InvalidOperationException(string message) {} }
  public class NotImplementedException : SystemException { public NotImplementedException() {} public NotImplementedException(string message) {} }
  public class NotSupportedException : SystemException { public NotSupportedException() {} public NotSupportedException(string message) {} }
  public class FormatException : SystemException { public FormatException() {} public FormatException(string message) {} }
}

namespace System.Collections {
  public interface IEnumerator { bool MoveNext(); object Current { get; } void Reset(); }
  public interface IEnumerable { IEnumerator GetEnumerator(); }
}

namespace System.Collections.Generic {
  public interface IEnumerator<out T> : IDisposable, System.Collections.IEnumerator { T Current { get; } }
  public interface IEnumerable<out T> : System.Collections.IEnumerable { new IEnumerator<T> GetEnumerator(); }
  public interface ICollection<T> : IEnumerable<T> { int Count { get; } }
  public interface IComparer<in T> { int Compare(T x, T y); }
  public interface IEqualityComparer<in T> { bool Equals(T x, T y); int GetHashCode(T obj); }
  public interface IList<T> : ICollection<T> { T this[int index] { get; set; } }
}

namespace System.Linq {
  /* Declared so that LINQ code binds and the emitter can refuse it by name: "write a loop". */
  public static class Enumerable {
    public static extern System.Collections.Generic.IEnumerable<TResult> Select<TSource, TResult>(this System.Collections.Generic.IEnumerable<TSource> source, Func<TSource, TResult> selector);
    public static extern System.Collections.Generic.IEnumerable<TSource> Where<TSource>(this System.Collections.Generic.IEnumerable<TSource> source, Func<TSource, bool> predicate);
    public static extern System.Collections.Generic.List<TSource> ToList<TSource>(this System.Collections.Generic.IEnumerable<TSource> source);
    public static extern TSource[] ToArray<TSource>(this System.Collections.Generic.IEnumerable<TSource> source);
    public static extern int Count<TSource>(this System.Collections.Generic.IEnumerable<TSource> source);
    public static extern bool Any<TSource>(this System.Collections.Generic.IEnumerable<TSource> source);
  }
}

namespace System.Runtime.CompilerServices {
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
  public sealed class ExtensionAttribute : Attribute {}
}
