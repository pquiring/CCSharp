/**
 Represents a file or folder.
*/

using System;

namespace System.IO {
  public class File {
    public File(String filename) {
      Name = filename;
      Create(filename);
    }
    private unsafe File(void* value) {
      Value = value;
    }
    ~File() {
      Destroy();
    }
    public extern bool Exists();
    public extern bool IsDirectory();
    public extern bool Copy(String dest);
    public extern File[] List();
    public extern bool MakeFolder();
    public extern bool MakeFolders();
    public extern bool DeleteFolder();

    private String Name;
    private unsafe void* Value;
    private extern void Create(String filename);
    private extern void Destroy();
  }
}
