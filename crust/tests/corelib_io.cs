using System;
using System.IO;
class Prog {
  public static int Main() {
    string dir = "/tmp/ccs_corelib_io_test";
    Directory.CreateDirectory(dir);
    Console.WriteLine(Directory.Exists(dir));
    string file = Path.Combine(dir, "note.txt");
    Console.WriteLine(file);
    Console.WriteLine(File.Exists(file));
    File.WriteAllText(file, "line one\nline two");
    Console.WriteLine(File.Exists(file));
    string back = File.ReadAllText(file);
    Console.WriteLine(back);
    Console.WriteLine(back.Length);
    Console.WriteLine(Path.GetFileName(file));
    Console.WriteLine(Path.GetDirectoryName(file));
    File.WriteAllText(file, "second " + back.Length);
    Console.WriteLine(File.ReadAllText(file));
    File.Delete(file);
    Console.WriteLine(File.Exists(file));
    return 0;
  }
}
