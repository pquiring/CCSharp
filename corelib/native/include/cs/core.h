/* cs/core.h -- the native half of the CC# corelib, in the C++ subset Crust lowers.
 *
 * Every function here backs a member declared in corelib/src (see the [Cpp] attributes there), written on
 * top of coost.  The conventions are coost's: overloads differ by argument count only, no default
 * arguments, no exceptions, no reference returns.
 *
 * Only the string core of coost (mem, fast, fastring) is reached from here, so a program that stays inside it
 * can also be compiled by shivyc; io.h and time.h pull in coost's fs / time, which need glibc headers that
 * shivyc does not bundle (<errno.h>).
 *
 * There is no `catch` in the Crust C# subset, so no program can observe an exception: a failure that .NET
 * would raise is an unhandled exception, which ends the process.  cs_fail does exactly that, and aborts so the
 * exit status (134) is the one .NET gives.
 */
#pragma once
#include <stdio.h>
#include <stdlib.h>
#include "co/fastring.h"

static inline void cs_fail(const char *msg) {
    fflush(stdout);                 /* .NET's console is unbuffered: what was printed before the failure is kept */
    fprintf(stderr, "Unhandled exception. %s\n", msg);
    abort();
}

/* ---- string ---------------------------------------------------------- */

static inline int cs_s_index_of(const fastring &s, const char *sub) {
    size_t r = s.find_cstr(sub);
    if (r == fastring::npos) { return -1; }
    return (int)r;
}

static inline int cs_s_last_index_of(const fastring &s, const char *sub) {
    size_t r = s.rfind_cstr(sub);
    if (r == fastring::npos) { return -1; }
    return (int)r;
}

static inline fastring cs_s_replace(const fastring &s, const char *from, const char *to) {
    fastring r(s);
    r.replace_cstr(from, to, 0);
    return r;
}

static inline fastring cs_s_upper(const fastring &s) { fastring r(s); r.toupper(); return r; }
static inline fastring cs_s_lower(const fastring &s) { fastring r(s); r.tolower(); return r; }
static inline fastring cs_s_trim(const fastring &s) { fastring r(s); r.trim(); return r; }

static inline fastring cs_s_substr1(const fastring &s, int start) {
    if (start < 0 || (size_t)start > s.size()) {
        cs_fail("System.ArgumentOutOfRangeException: startIndex cannot be larger than length of string.");
    }
    fastring r = s.substr((size_t)start);
    return r;
}

static inline fastring cs_s_substr2(const fastring &s, int start, int len) {
    if (start < 0 || len < 0 || (size_t)start + (size_t)len > s.size()) {
        cs_fail("System.ArgumentOutOfRangeException: Index and length must refer to a location within the string.");
    }
    fastring r = s.substr((size_t)start, (size_t)len);
    return r;
}

/* ---- numbers <-> string ---------------------------------------------- */

static inline fastring cs_i64_str(long long v) { fastring r; r.append_int(v); return r; }
static inline fastring cs_u64_str(unsigned long long v) { fastring r; r.append_uint(v); return r; }
static inline fastring cs_bool_str(bool v) {
    fastring r;
    if (v) { r.append_cstr("True"); } else { r.append_cstr("False"); }
    return r;
}

static inline bool cs_is_space(char c) { return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\v' || c == '\f'; }

/* Out-parameters of scalar type are a reference parameter, which cpprust lowers for methods but not for
 * free functions, so these live in a class of static methods. */
class CsNum {
public:
/* [ws] [+|-] digits [ws]  -- what int.Parse accepts.  Returns false on anything else, and on overflow. */
static bool parse_i64(const fastring &s, long long lo, long long hi, long long &out) {
    size_t i = 0;
    size_t n = s.size();
    const char *p = s.c_str();
    while (i < n && cs_is_space(p[i])) { i = i + 1; }
    bool neg = false;
    if (i < n && (p[i] == '-' || p[i] == '+')) { neg = p[i] == '-'; i = i + 1; }
    if (i >= n || p[i] < '0' || p[i] > '9') { return false; }
    unsigned long long limit = neg ? (unsigned long long)hi + 1ULL : (unsigned long long)hi;
    unsigned long long v = 0;
    while (i < n && p[i] >= '0' && p[i] <= '9') {
        unsigned long long d = (unsigned long long)(p[i] - '0');
        if (v > (limit - d) / 10ULL) { return false; }
        v = v * 10ULL + d;
        i = i + 1;
    }
    while (i < n && cs_is_space(p[i])) { i = i + 1; }
    if (i != n) { return false; }
    if (neg) { out = (long long)(0ULL - v); } else { out = (long long)v; }
    if (out < lo || out > hi) { return false; }
    return true;
}

static bool try_parse_i32(const fastring &s, int &out) {
    long long v = 0;
    if (!parse_i64(s, -2147483647LL - 1LL, 2147483647LL, v)) { return false; }
    out = (int)v;
    return true;
}
static bool try_parse_i64(const fastring &s, long long &out) {
    return parse_i64(s, -9223372036854775807LL - 1LL, 9223372036854775807LL, out);
}
};

static inline int cs_parse_i32(const fastring &s) {
    int v = 0;
    if (!CsNum::try_parse_i32(s, v)) { cs_fail("System.FormatException: The input string was not in a correct format."); }
    return v;
}
static inline long long cs_parse_i64_or_fail(const fastring &s) {
    long long v = 0;
    if (!CsNum::try_parse_i64(s, v)) { cs_fail("System.FormatException: The input string was not in a correct format."); }
    return v;
}
