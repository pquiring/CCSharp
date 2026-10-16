using Crust;

namespace System {

  /*
    string is coost's fastring, held by value.  A C# string is immutable, so a copy cannot be told from
    sharing; that is what makes by-value sound where it is not for a class.

    There is no null string: a field is the empty string until assigned, and `null` is refused.
  */
  [Cpp("fastring"), CppInclude("\"cs/core.h\"")]
  public sealed class String {
    public static readonly string Empty;

    [Cpp("(int){this}.size()")]                       public extern int Length { get; }
    public extern char this[int index] { get; }                                                   // char: refused

    [Cpp("cs_s_substr1({this}, {0})")]                public extern string Substring(int startIndex);
    [Cpp("cs_s_substr2({this}, {0}, {1})")]           public extern string Substring(int startIndex, int length);
    [Cpp("cs_s_index_of({this}, {0:c})")]             public extern int IndexOf(string value);
    [Cpp("cs_s_last_index_of({this}, {0:c})")]        public extern int LastIndexOf(string value);
    [Cpp("{this}.contains_cstr({0:c})")]              public extern bool Contains(string value);
    [Cpp("{this}.starts_with_cstr({0:c})")]           public extern bool StartsWith(string value);
    [Cpp("{this}.ends_with_cstr({0:c})")]             public extern bool EndsWith(string value);
    [Cpp("cs_s_replace({this}, {0:c}, {1:c})")]       public extern string Replace(string oldValue, string newValue);
    [Cpp("cs_s_upper({this})")]                       public extern string ToUpper();
    [Cpp("cs_s_lower({this})")]                       public extern string ToLower();
    [Cpp("cs_s_trim({this})")]                        public extern string Trim();
    [Cpp("{this}.eq_cstr({0:c})")]                    public extern bool Equals(string value);
    [Cpp("{this}")]                                   public extern string ToString();
    [Cpp("{0}.empty()")]                              public static extern bool IsNullOrEmpty(string value);

    // The rest of the .NET surface: declared, not implemented yet.
    public extern string[] Split(string separator);
    public extern int IndexOf(char value);
    public extern int CompareTo(string other);
    public extern string PadLeft(int totalWidth);
    public extern string PadRight(int totalWidth);
    public extern string Insert(int startIndex, string value);
    public extern string Remove(int startIndex);
    public extern char[] ToCharArray();
    public static extern string Join(string separator, string[] values);
    // Declared for BINDING only: Roslyn lowers `$"{a} {b} {c}"` to the Format overload of matching arity, and fails to compile if
    // it is missing. The emitter writes interpolated strings itself and never calls these, and a direct call is refused by name.
    public static extern string Format(string format, object arg0);
    public static extern string Format(string format, object arg0, object arg1);
    public static extern string Format(string format, object arg0, object arg1, object arg2);
    public static extern string Format(string format, params object[] args);
    public static extern string Concat(string a, string b);
    public static extern bool IsNullOrWhiteSpace(string value);
  }
}

namespace System.Text {

  /* A StringBuilder is a coost fastring appended to in place.  It is a class, so it is borrowed by a callee. */
  [Cpp("fastring"), CppInclude("\"cs/core.h\"")]
  public sealed class StringBuilder {
    public StringBuilder() {}

    [Cpp("(int){this}.size()")]                                          public extern int Length { get; }
    [Cpp("{this}.append_str({0})"), CppFluent]                           public extern StringBuilder Append(string value);
    [Cpp("{this}.append_int((long long)({0}))"), CppFluent]              public extern StringBuilder Append(int value);
    [Cpp("{this}.append_int((long long)({0}))"), CppFluent]              public extern StringBuilder Append(long value);
    [Cpp("{this}.append_uint((unsigned long long)({0}))"), CppFluent]    public extern StringBuilder Append(uint value);
    [Cpp("{this}.append_uint((unsigned long long)({0}))"), CppFluent]    public extern StringBuilder Append(ulong value);
    [Cpp("{this}.append_cstr(({0}) ? \"True\" : \"False\")"), CppFluent] public extern StringBuilder Append(bool value);
    [Cpp("{this}.append_str({0}); {this}.append_char('\\n')"), CppFluent] public extern StringBuilder AppendLine(string value);
    [Cpp("{this}.append_char('\\n')"), CppFluent]                        public extern StringBuilder AppendLine();
    [Cpp("{this}.clear()"), CppFluent]                                   public extern StringBuilder Clear();
    [Cpp("{this}")]                                                      public extern string ToString();

    public extern StringBuilder Insert(int index, string value);
    public extern StringBuilder Replace(string oldValue, string newValue);
    public extern StringBuilder Append(char value);
  }
}
