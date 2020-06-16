using System;

public class Example {
  public static int Count = 1024;
  public static int Main(String[] args) {
    String s1 = "--";
    String s2 = "++";
    long start = DateTime.CurrentTimeEpoch();
    Array<String> al = new Array<String>();
    for(int x=0;x<Count*32;x++) {
      al.Add(s1);
      int cnt = al.Size();
      for(int y=0;y<cnt;y++) {
        String e = al.Get(y);
        if (e.Equals(s2)) {
          Console.Out.WriteLine("ok");
        }
      }
    }
    long stop = DateTime.CurrentTimeEpoch();
    Console.Out.WriteLine("test4=" + (stop - start));
    return 0;
  }
}
