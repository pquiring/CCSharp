using System;

// A uses the static variable of Log, which is declared in a file that Main (in A) also uses: Log has to come first, or the variable is used before it exists
class Worker {
  public int Done;
  public void Run(int n) { for (int i = 0; i < n; i++) { Log.Count++; int r = Log.Next() % 7; Done += r; } }
}

class Program {
  static int Main() {
    Worker w = new Worker();
    w.Run(5);
    Console.WriteLine(Log.Count + " " + w.Done);
    return Log.Summarise(w.Done);
  }
}
