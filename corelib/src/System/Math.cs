using Crust;

namespace System {
  [CppInclude("\"cs/math.h\"")]
  public static class Math {
    public const double PI = 3.1415926535897931;
    public const double E = 2.7182818284590451;

    [Cpp("cs_abs_i32({0})")]      public static extern int Abs(int value);
    [Cpp("cs_abs_i64({0})")]      public static extern long Abs(long value);
    [Cpp("cs_abs_f64({0})")]      public static extern double Abs(double value);
    [Cpp("cs_abs_f32({0})")]      public static extern float Abs(float value);
    [Cpp("cs_max_i32({0}, {1})")] public static extern int Max(int a, int b);
    [Cpp("cs_max_i64({0}, {1})")] public static extern long Max(long a, long b);
    [Cpp("cs_max_f64({0}, {1})")] public static extern double Max(double a, double b);
    [Cpp("cs_max_f32({0}, {1})")] public static extern float Max(float a, float b);
    [Cpp("cs_min_i32({0}, {1})")] public static extern int Min(int a, int b);
    [Cpp("cs_min_i64({0}, {1})")] public static extern long Min(long a, long b);
    [Cpp("cs_min_f64({0}, {1})")] public static extern double Min(double a, double b);
    [Cpp("cs_min_f32({0}, {1})")] public static extern float Min(float a, float b);
    [Cpp("cs_sign_i32({0})")]     public static extern int Sign(int value);

    [Cpp("cs_sqrt({0})")]         public static extern double Sqrt(double d);
    [Cpp("cs_floor({0})")]        public static extern double Floor(double d);
    [Cpp("cs_ceil({0})")]         public static extern double Ceiling(double d);
    [Cpp("cs_pow({0}, {1})")]     public static extern double Pow(double x, double y);
    [Cpp("cs_sin({0})")]          public static extern double Sin(double a);
    [Cpp("cs_cos({0})")]          public static extern double Cos(double a);

    [Cpp("cs_tan({0})")]          public static extern double Tan(double a);
    [Cpp("cs_atan({0})")]         public static extern double Atan(double a);
    [Cpp("cs_atan2({0}, {1})")]   public static extern double Atan2(double y, double x);
    [Cpp("cs_asin({0})")]         public static extern double Asin(double a);
    [Cpp("cs_acos({0})")]         public static extern double Acos(double a);
    [Cpp("cs_exp({0})")]          public static extern double Exp(double a);
    [Cpp("cs_log({0})")]          public static extern double Log(double a);

    // Clamp throws ArgumentException when min > max in .NET; here that is a fatal error, like every other unhandled exception.
    [Cpp("cs_clamp_i32({0}, {1}, {2})")] public static extern int Clamp(int value, int min, int max);
    [Cpp("cs_clamp_i64({0}, {1}, {2})")] public static extern long Clamp(long value, long min, long max);
    [Cpp("cs_clamp_f32({0}, {1}, {2})")] public static extern float Clamp(float value, float min, float max);
    [Cpp("cs_clamp_f64({0}, {1}, {2})")] public static extern double Clamp(double value, double min, double max);

    public static extern double Round(double a);             // banker's rounding: not implemented yet
  }

  /* System.MathF: single precision. float arithmetic is native in Crust; these map to the C99 ...f functions. */
  [CppInclude("\"cs/math.h\"")]
  public static class MathF {
    public const float PI = 3.14159265358979f;
    public const float E = 2.71828182845905f;

    [Cpp("cs_abs_f32({0})")]      public static extern float Abs(float value);
    [Cpp("cs_max_f32({0}, {1})")] public static extern float Max(float a, float b);
    [Cpp("cs_min_f32({0}, {1})")] public static extern float Min(float a, float b);
    [Cpp("cs_sqrtf({0})")]        public static extern float Sqrt(float x);
    [Cpp("cs_floorf({0})")]       public static extern float Floor(float x);
    [Cpp("cs_ceilf({0})")]        public static extern float Ceiling(float x);
    [Cpp("cs_powf({0}, {1})")]    public static extern float Pow(float x, float y);
    [Cpp("cs_sinf({0})")]         public static extern float Sin(float x);
    [Cpp("cs_cosf({0})")]         public static extern float Cos(float x);
    [Cpp("cs_tanf({0})")]         public static extern float Tan(float x);
    [Cpp("cs_atanf({0})")]        public static extern float Atan(float x);
    [Cpp("cs_atan2f({0}, {1})")]  public static extern float Atan2(float y, float x);
    [Cpp("cs_asinf({0})")]        public static extern float Asin(float x);
    [Cpp("cs_acosf({0})")]        public static extern float Acos(float x);
    [Cpp("cs_expf({0})")]         public static extern float Exp(float x);
    [Cpp("cs_logf({0})")]         public static extern float Log(float x);
  }
}
