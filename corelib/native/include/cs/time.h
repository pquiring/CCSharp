/* cs/time.h -- clocks on coost's time. */
#pragma once
#include "cs/core.h"
#include "co/time.h"

/* ---- time -------------------------------------------------------------- */

static inline long long cs_tick_ms() { return now::ms(); }

static inline co::Timer cs_stopwatch_new() { co::Timer t; return t; }
