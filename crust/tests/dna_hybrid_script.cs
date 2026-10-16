// dna
using System;
using System.Collections.Generic;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }

// Nothing here is marked: the engine is plain Crust-subset code and stays native; the script uses a lambda and try/catch, which Crust has
// not, so it is managed, and everything that uses the script (the sink that pools it, and Main) follows it.

[MaxInstances(8)]
class Node {
  public int Id;
  public int Bumps;
  public float X;
  public Node(int id) { Id = id; }
  public void Bump() { Bumps++; X += 0.5f; }
}

// the engine produces calls; the game's sink pulls and performs them
[MaxInstances(1)]
class Scene {
  List<Node> nodes;
  public int CallKind;
  public Node CallNode;
  int next;
  public Scene() { nodes = new List<Node>(); }
  public void Add(Node n) { nodes.Add(n); }
  public int Count() { return nodes.Count; }
  public void Begin(int kind) { CallKind = kind; next = 0; }
  public bool NextCall() {
    if (next >= nodes.Count) return false;
    CallNode = nodes[next];
    next++;
    return true;
  }
}

[MaxInstances(4)]
class Ball {
  public Node Self;
  public int Hits;
  public int Score;
  public void Update() {
    Func<int, int> bonus = k => k * 2 + Self.Id;
    try {
      Score += bonus(Hits);
      Hits++;
      Self.Bump();
      if (Hits > 3) throw new InvalidOperationException("done");
    } catch (InvalidOperationException) {
      Score = -Score;
    }
  }
}

class Scripts {
  static Ball[] pool;
  static int high;
  public static void Init() { pool = new Ball[4]; high = 0; }
  public static Ball AddBall(Node n) { Ball b = new Ball(); b.Self = n; pool[high] = b; high++; return b; }
  static Ball Find(Node n) { for (int i = 0; i < high; i++) if (pool[i].Self == n) return pool[i]; return null; }
  public static void Pump(Scene scene) {
    while (scene.NextCall()) {
      Ball b = Find(scene.CallNode);
      if (b != null) b.Update();
    }
  }
}

class Program {
  static int Main() {
    Scripts.Init();
    Scene scene = new Scene();
    Node n1 = new Node(1);
    Node n2 = new Node(2);
    Node plain = new Node(3);
    scene.Add(n1); scene.Add(n2); scene.Add(plain);
    Ball b1 = Scripts.AddBall(n1);
    Ball b2 = Scripts.AddBall(n2);
    for (int frame = 0; frame < 6; frame++) {
      scene.Begin(frame);
      Scripts.Pump(scene);
      Console.WriteLine(frame + ": " + b1.Hits + " " + b1.Score + " | " + b2.Hits + " " + b2.Score + " | " + n1.Bumps + " " + n2.Bumps + " " + plain.Bumps);
    }
    Console.WriteLine((int)(n1.X * 10f) + " " + (int)(n2.X * 10f) + " " + scene.Count());
    return b1.Hits + b2.Hits;
  }
}
