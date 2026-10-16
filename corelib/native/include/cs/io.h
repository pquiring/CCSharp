/* cs/io.h -- System.IO on coost's fs / path.  (Needs <errno.h>: compile the lowered C with a C compiler.) */
#pragma once
#include "cs/core.h"
#include "co/fs.h"
#include "co/path.h"

/* ---- System.IO --------------------------------------------------------- */

static inline bool cs_file_exists(const fastring &p) { return fs::exists(p.c_str()) && !fs::isdir(p.c_str()); }
static inline bool cs_dir_exists(const fastring &p) { return fs::isdir(p.c_str()); }
static inline void cs_dir_create(const fastring &p) { fs::mkdir(p.c_str(), true); }
static inline void cs_file_delete(const fastring &p) { fs::remove(p.c_str(), false); }

static inline fastring cs_file_read(const fastring &p) {
    if (!cs_file_exists(p)) { cs_fail("System.IO.FileNotFoundException: Could not find file."); }
    fs::file f(p.c_str(), 'r');
    fastring r = f.read_str((size_t)f.size());
    f.close();
    return r;
}

static inline void cs_file_write(const fastring &p, const fastring &text) {
    fs::file f(p.c_str(), 'w');
    if (!f.is_open()) { cs_fail("System.IO.IOException: Could not open file for writing."); }
    f.write_str(text);
    f.close();
}

static inline fastring cs_path_combine(const fastring &a, const fastring &b) {
    if (b.starts_with_char('/') || a.empty()) { fastring r(b); return r; }
    fastring r(a);
    if (!r.ends_with_char('/')) { r.append_char('/'); }
    r.append_str(b);
    return r;
}
static inline fastring cs_path_filename(const fastring &p) { return path::base(p.data(), p.size()); }
static inline fastring cs_path_dirname(const fastring &p) { return path::dir(p.data(), p.size()); }
