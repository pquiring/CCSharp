using Crust;

/*
  The built-in value types.  Constants are folded by Roslyn, so `int.MaxValue` and friends need no C++.
  ToString / Parse / TryParse are the parts that need native code (cs/core.h, on coost's fastring).
*/
namespace System {

  public struct Boolean {
    [Cpp("cs_bool_str({this})"), CppInclude("\"cs/core.h\"")] public extern string ToString();
  }

  public struct Byte {
    public const byte MinValue = 0;
    public const byte MaxValue = 255;
    [Cpp("cs_i64_str({this})"), CppInclude("\"cs/core.h\"")] public extern string ToString();
  }
  public struct SByte {
    public const sbyte MinValue = -128;
    public const sbyte MaxValue = 127;
    [Cpp("cs_i64_str({this})"), CppInclude("\"cs/core.h\"")] public extern string ToString();
  }
  public struct Int16 {
    public const short MinValue = -32768;
    public const short MaxValue = 32767;
    [Cpp("cs_i64_str({this})"), CppInclude("\"cs/core.h\"")] public extern string ToString();
  }
  public struct UInt16 {
    public const ushort MinValue = 0;
    public const ushort MaxValue = 65535;
    [Cpp("cs_i64_str({this})"), CppInclude("\"cs/core.h\"")] public extern string ToString();
  }

  public struct Int32 {
    public const int MinValue = -2147483648;
    public const int MaxValue = 2147483647;
    [Cpp("cs_i64_str({this})"), CppInclude("\"cs/core.h\"")] public extern string ToString();
    [Cpp("cs_parse_i32({0})"), CppInclude("\"cs/core.h\"")] public static extern int Parse(string s);
    [Cpp("CsNum::try_parse_i32({0}, {1})"), CppInclude("\"cs/core.h\"")] public static extern bool TryParse(string s, out int result);
  }
  public struct UInt32 {
    public const uint MinValue = 0;
    public const uint MaxValue = 4294967295;
    [Cpp("cs_u64_str({this})"), CppInclude("\"cs/core.h\"")] public extern string ToString();
  }
  public struct Int64 {
    public const long MinValue = -9223372036854775808;
    public const long MaxValue = 9223372036854775807;
    [Cpp("cs_i64_str({this})"), CppInclude("\"cs/core.h\"")] public extern string ToString();
    [Cpp("cs_parse_i64_or_fail({0})"), CppInclude("\"cs/core.h\"")] public static extern long Parse(string s);
    [Cpp("CsNum::try_parse_i64({0}, {1})"), CppInclude("\"cs/core.h\"")] public static extern bool TryParse(string s, out long result);
  }
  public struct UInt64 {
    public const ulong MinValue = 0;
    public const ulong MaxValue = 18446744073709551615;
    [Cpp("cs_u64_str({this})"), CppInclude("\"cs/core.h\"")] public extern string ToString();
  }

  // float / double: arithmetic and comparison work; converting them to text does not (C# prints the shortest
  // round-trip form, which printf cannot reproduce), so there is no ToString and the emitter refuses printing.
  public struct Single {
    public const float MinValue = -3.40282347E+38f;
    public const float MaxValue = 3.40282347E+38f;
    public const float Epsilon = 1.401298E-45f;
    public const float PositiveInfinity = 1.0f / 0.0f;
    public const float NegativeInfinity = -1.0f / 0.0f;
    public const float NaN = 0.0f / 0.0f;
    [Cpp("({0} != {0})")]                                public static extern bool IsNaN(float v);
    [Cpp("({0} == 1.0f / 0.0f)")]                    public static extern bool IsPositiveInfinity(float v);
    [Cpp("({0} == -1.0f / 0.0f)")]                   public static extern bool IsNegativeInfinity(float v);
    [Cpp("({0} == 1.0f / 0.0f || {0} == -1.0f / 0.0f)")]      public static extern bool IsInfinity(float v);
    public extern string ToString();
  }
  public struct Double {
    public const double MinValue = -1.7976931348623157E+308;
    public const double MaxValue = 1.7976931348623157E+308;
    public const double Epsilon = 4.94065645841247E-324;
    public const double PositiveInfinity = 1.0 / 0.0;
    public const double NegativeInfinity = -1.0 / 0.0;
    public const double NaN = 0.0 / 0.0;
    [Cpp("({0} != {0})")]                                public static extern bool IsNaN(double v);
    [Cpp("({0} == 1.0 / 0.0)")]                    public static extern bool IsPositiveInfinity(double v);
    [Cpp("({0} == -1.0 / 0.0)")]                   public static extern bool IsNegativeInfinity(double v);
    [Cpp("({0} == 1.0 / 0.0 || {0} == -1.0 / 0.0)")]      public static extern bool IsInfinity(double v);
    public extern string ToString();
  }

  // `char` is declared so that code binds, and refused by the emitter: a C# char is UTF-16, a C char is a byte.
  public struct Char {
    public const char MinValue = '\0';
    public const char MaxValue = '\uffff';
    public extern string ToString();
  }
  public struct Decimal {}
  public struct IntPtr {}
  public struct UIntPtr {}
}
