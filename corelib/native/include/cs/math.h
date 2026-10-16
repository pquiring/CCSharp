/* cs/math.h -- System.Math.  Plain value functions (no references, no coost): safe for shivyc too. */
#pragma once
#include <math.h>
#include "cs/core.h"

static inline int cs_abs_i32(int v) {
    if (v == -2147483647 - 1) { cs_fail("System.OverflowException: Negating the minimum value of a twos complement number is invalid."); }
    if (v < 0) { return -v; }
    return v;
}
static inline long long cs_abs_i64(long long v) {
    if (v == -9223372036854775807LL - 1LL) { cs_fail("System.OverflowException: Negating the minimum value of a twos complement number is invalid."); }
    if (v < 0) { return -v; }
    return v;
}
static inline double cs_abs_f64(double v) { if (v < 0.0) { return -v; } return v; }
static inline int cs_max_i32(int a, int b) { if (a > b) { return a; } return b; }
static inline long long cs_max_i64(long long a, long long b) { if (a > b) { return a; } return b; }
static inline double cs_max_f64(double a, double b) { if (a > b) { return a; } return b; }
static inline int cs_min_i32(int a, int b) { if (a < b) { return a; } return b; }
static inline long long cs_min_i64(long long a, long long b) { if (a < b) { return a; } return b; }
static inline double cs_min_f64(double a, double b) { if (a < b) { return a; } return b; }
static inline int cs_sign_i32(int v) { if (v > 0) { return 1; } if (v < 0) { return -1; } return 0; }

static inline double cs_sqrt(double d) { return sqrt(d); }
static inline double cs_floor(double d) { return floor(d); }
static inline double cs_ceil(double d) { return ceil(d); }
static inline double cs_pow(double x, double y) { return pow(x, y); }
static inline double cs_sin(double a) { return sin(a); }
static inline double cs_cos(double a) { return cos(a); }

/* ---- Math.Clamp / more of Math ------------------------------------------------------------------------------------------- */
static inline int cs_clamp_i32(int v, int lo, int hi) {
    if (lo > hi) { cs_fail("System.ArgumentException: Min cannot be greater than max."); }
    if (v < lo) { return lo; } if (v > hi) { return hi; } return v;
}
static inline long long cs_clamp_i64(long long v, long long lo, long long hi) {
    if (lo > hi) { cs_fail("System.ArgumentException: Min cannot be greater than max."); }
    if (v < lo) { return lo; } if (v > hi) { return hi; } return v;
}
static inline float cs_clamp_f32(float v, float lo, float hi) {
    if (lo > hi) { cs_fail("System.ArgumentException: Min cannot be greater than max."); }
    if (v < lo) { return lo; } if (v > hi) { return hi; } return v;
}
static inline double cs_clamp_f64(double v, double lo, double hi) {
    if (lo > hi) { cs_fail("System.ArgumentException: Min cannot be greater than max."); }
    if (v < lo) { return lo; } if (v > hi) { return hi; } return v;
}
static inline double cs_tan(double a) { return tan(a); }
static inline double cs_atan(double a) { return atan(a); }
static inline double cs_atan2(double y, double x) { return atan2(y, x); }
static inline double cs_asin(double a) { return asin(a); }
static inline double cs_acos(double a) { return acos(a); }
static inline double cs_exp(double a) { return exp(a); }
static inline double cs_log(double a) { return log(a); }

/* ---- System.MathF: the single-precision twins ---------------------------------------------------------------------------- */
static inline float cs_abs_f32(float v) { if (v < 0.0f) { return -v; } return v; }
static inline float cs_max_f32(float a, float b) { if (a > b) { return a; } return b; }
static inline float cs_min_f32(float a, float b) { if (a < b) { return a; } return b; }
static inline float cs_sqrtf(float d) { return sqrtf(d); }
static inline float cs_floorf(float d) { return floorf(d); }
static inline float cs_ceilf(float d) { return ceilf(d); }
static inline float cs_powf(float x, float y) { return powf(x, y); }
static inline float cs_sinf(float a) { return sinf(a); }
static inline float cs_cosf(float a) { return cosf(a); }
static inline float cs_tanf(float a) { return tanf(a); }
static inline float cs_atanf(float a) { return atanf(a); }
static inline float cs_atan2f(float y, float x) { return atan2f(y, x); }
static inline float cs_asinf(float a) { return asinf(a); }
static inline float cs_acosf(float a) { return acosf(a); }
static inline float cs_expf(float a) { return expf(a); }
static inline float cs_logf(float a) { return logf(a); }

/* C# `%` on floating point is the truncated remainder, which is fmod; C has no % for floats. The emitter lowers `a % b` to these. */
static inline float cs_fmodf(float a, float b) { return fmodf(a, b); }
static inline double cs_fmod(double a, double b) { return fmod(a, b); }
