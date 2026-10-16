using Crust;

namespace System.IO {
  [CppInclude("\"cs/io.h\"")]
  public static class File {
    [Cpp("cs_file_exists({0})")]               public static extern bool Exists(string path);
    [Cpp("cs_file_read({0})")]                 public static extern string ReadAllText(string path);
    [Cpp("cs_file_write({0}, {1})")]           public static extern void WriteAllText(string path, string contents);
    [Cpp("cs_file_delete({0})")]               public static extern void Delete(string path);
    public static extern string[] ReadAllLines(string path);          // not implemented yet
  }
  [CppInclude("\"cs/io.h\"")]
  public static class Directory {
    [Cpp("cs_dir_exists({0})")]                public static extern bool Exists(string path);
    [Cpp("cs_dir_create({0})")]                public static extern void CreateDirectory(string path);
  }
  [CppInclude("\"cs/io.h\"")]
  public static class Path {
    [Cpp("cs_path_combine({0}, {1})")]         public static extern string Combine(string path1, string path2);
    [Cpp("cs_path_filename({0})")]             public static extern string GetFileName(string path);
    [Cpp("cs_path_dirname({0})")]              public static extern string GetDirectoryName(string path);
    public static extern string GetExtension(string path);            // not implemented yet
  }
}
