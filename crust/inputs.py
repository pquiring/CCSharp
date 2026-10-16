"""inputs -- what a C# program is made of: files, folders, .csproj and .sln, expanded to a list of .cs files.

    expand(["src/", "extra/Helpers.cs", "App/App.csproj", "Everything.sln"])  ->  ["/abs/a.cs", "/abs/b.cs", ...]

Rules (they follow MSBuild's, so a project converts the way it builds):

  * a .cs file                 is itself
  * a folder                   every .cs under it, recursively, except bin/, obj/, .git/ and .vs/
  * a .csproj
      SDK-style (<Project Sdk=..>)   every .cs under the project's folder (EnableDefaultCompileItems=false turns that
                                     off), except bin/ and obj/ and any folder that holds another project,
      then <Compile Include=".."> adds (wildcards and `..\\` paths work), <Compile Remove=".."> removes,
      and every <ProjectReference> is followed, once, transitively.
      Old-style projects have no default files: only their <Compile Include> items count.
  * a .sln                     the .csproj files it lists, each as above (merged into ONE program)
  * a wildcard                 `src/**/*.cs`, expanded here when the shell did not

Conditions on items (Condition="..") are not evaluated: every item is taken.  The result is sorted and unique.
"""
import fnmatch
import glob
import os
import re
import xml.etree.ElementTree as ET

SKIP_DIRS = {"bin", "obj", ".git", ".vs", "node_modules"}


class InputError(Exception):
    pass


def _norm(p):
    return os.path.normpath(p.replace("\\", "/"))


def _tag(el):
    return el.tag.split("}")[-1]


def _walk_cs(folder, skip_project_dirs=False, root=None):
    """every .cs under `folder`, pruning bin/obj/.git/.vs (and folders holding another project when asked)."""
    out = []
    root = root or folder
    for d, dirs, files in os.walk(folder):
        keep = []
        for x in sorted(dirs):
            if x in SKIP_DIRS:
                continue
            full = os.path.join(d, x)
            if skip_project_dirs and any(f.endswith((".csproj", ".vbproj", ".fsproj")) for f in os.listdir(full)):
                continue
            keep.append(x)
        dirs[:] = keep
        for f in sorted(files):
            if f.endswith(".cs"):
                out.append(os.path.join(d, f))
    return out


def _glob(pattern, base):
    """a project-relative pattern -> files; `**` matches any depth, a missing literal path is an error."""
    pat = _norm(os.path.join(base, pattern)) if not os.path.isabs(pattern) else _norm(pattern)
    if any(c in pat for c in "*?["):
        return [os.path.normpath(p) for p in glob.glob(pat, recursive=True) if os.path.isfile(p)]
    if not os.path.isfile(pat):
        raise InputError("a <Compile> item names a file that is not there: %s" % pat)
    return [pat]


def _expand_vars(s, proj):
    d = os.path.dirname(proj)
    return (s.replace("$(MSBuildProjectDirectory)", d).replace("$(MSBuildThisFileDirectory)", d + "/")
             .replace("$(ProjectDir)", d + "/"))


def project_files(proj, seen=None):
    """the .cs files of one .csproj and of the projects it references."""
    proj = os.path.abspath(proj)
    seen = seen if seen is not None else set()
    if proj in seen:
        return []
    seen.add(proj)
    if not os.path.isfile(proj):
        raise InputError("project not found: %s" % proj)
    try:
        root = ET.parse(proj).getroot()
    except ET.ParseError as e:
        raise InputError("%s is not valid XML: %s" % (proj, e))
    base = os.path.dirname(proj)
    sdk = "Sdk" in root.attrib or any(_tag(e) == "Sdk" for e in root)
    default_items = sdk
    for pg in root:
        if _tag(pg) == "PropertyGroup":
            for el in pg:
                if _tag(el) == "EnableDefaultCompileItems" and (el.text or "").strip().lower() == "false":
                    default_items = False
    files = []
    if default_items:
        files += _walk_cs(base, skip_project_dirs=True)
    removes = []
    refs = []
    for ig in root:
        if _tag(ig) != "ItemGroup":
            continue
        for it in ig:
            t = _tag(it)
            if t == "Compile":
                if "Include" in it.attrib:
                    for pat in _expand_vars(it.attrib["Include"], proj).split(";"):
                        if pat.strip():
                            files += _glob(pat.strip(), base)
                if "Remove" in it.attrib:
                    for pat in _expand_vars(it.attrib["Remove"], proj).split(";"):
                        if pat.strip():
                            removes.append(pat.strip())
            elif t == "ProjectReference" and "Include" in it.attrib:
                refs.append(os.path.normpath(os.path.join(base, _norm(it.attrib["Include"]))))
    if removes:
        drop = set()
        for pat in removes:
            full = _norm(os.path.join(base, pat))
            if any(c in full for c in "*?["):
                drop |= {os.path.normpath(p) for p in glob.glob(full, recursive=True)}
            else:
                drop.add(os.path.normpath(full))
        files = [f for f in files if os.path.normpath(f) not in drop]
    for r in refs:
        files += project_files(r, seen)
    return files


def solution_projects(sln):
    """the .csproj paths a .sln (or .slnx) lists."""
    base = os.path.dirname(os.path.abspath(sln))
    with open(sln, errors="replace") as f:
        text = f.read()
    found = re.findall(r'"([^"]+\.csproj)"', text)               # .sln: Project(..) = "Name", "path\x.csproj", ".."
    found += re.findall(r'Path="([^"]+\.csproj)"', text)         # .slnx
    out = []
    for p in found:
        full = os.path.normpath(os.path.join(base, _norm(p)))
        if full not in out:
            out.append(full)
    return out


def expand(inputs):
    """files / folders / .csproj / .sln / wildcards -> sorted unique absolute .cs paths."""
    if isinstance(inputs, str):
        inputs = [inputs]
    if not inputs:
        raise InputError("no input: give a .cs file, a folder, a .csproj or a .sln")
    files = []
    seen = set()
    for item in inputs:
        if os.path.isdir(item):
            files += _walk_cs(os.path.abspath(item))
        elif os.path.isfile(item):
            low = item.lower()
            if low.endswith(".cs"):
                files.append(os.path.abspath(item))
            elif low.endswith(".csproj"):
                files += project_files(item, seen)
            elif low.endswith((".sln", ".slnx")):
                projs = solution_projects(item)
                if not projs:
                    raise InputError("%s lists no .csproj projects" % item)
                for p in projs:
                    files += project_files(p, seen)
            else:
                raise InputError("don't know how to read %s (expected .cs, .csproj, .sln or a folder)" % item)
        elif any(c in item for c in "*?["):
            hits = [h for h in glob.glob(item, recursive=True) if h.endswith(".cs")]
            if not hits:
                raise InputError("nothing matches %s" % item)
            files += [os.path.abspath(h) for h in hits]
        else:
            raise InputError("no such file or folder: %s" % item)
    out = sorted({os.path.abspath(f) for f in files})
    if not out:
        raise InputError("no C# files found in: %s" % " ".join(inputs))
    return out


def project_name(inputs):
    """a name for the program: the first input's stem (App.csproj -> App; a folder called src takes its parent's name)."""
    first = os.path.abspath(inputs[0] if not isinstance(inputs, str) else inputs)
    name = os.path.basename(first)
    stem, ext = os.path.splitext(name)
    if ext.lower() in (".cs", ".csproj", ".sln", ".slnx"):
        name = stem
    elif name in ("src", "source", "Source"):
        name = os.path.basename(os.path.dirname(first))
    return re.sub(r"\W", "_", name) or "Program"
