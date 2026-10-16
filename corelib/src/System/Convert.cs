using Crust;

namespace System {
  public static class Convert {
    [Cpp("cs_parse_i32({0})"), CppInclude("\"cs/core.h\"")]         public static extern int ToInt32(string value);
    [Cpp("cs_parse_i64_or_fail({0})"), CppInclude("\"cs/core.h\"")] public static extern long ToInt64(string value);
    [Cpp("cs_i64_str({0})"), CppInclude("\"cs/core.h\"")]           public static extern string ToString(int value);
    [Cpp("cs_i64_str({0})"), CppInclude("\"cs/core.h\"")]           public static extern string ToString(long value);
    [Cpp("cs_bool_str({0})"), CppInclude("\"cs/core.h\"")]          public static extern string ToString(bool value);
  }
}
