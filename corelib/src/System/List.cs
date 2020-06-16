using System.Collections;

namespace System {

  /** Linked-list of Objects.
  * Slow to add, fast to remove.
  */
  public class List<T> where T : class{
    public class Node<E> {
      public Node<E> Prev;
      public Node<E> Next;
      public E Value;
    }
    private Node<T> Head, Tail;
    private int Length;
    public void Add(T Value) {
      Node<T> node = new Node<T>();
      node.Value = Value;
      if (Tail == null) {
        Tail = node;
        Head = node;
      } else {
        node.Prev = Tail;
        Tail.Next = node;
        Tail = node;
      }
      Length++;
    }
    public void Remove(T Value) {
      Node<T> node = Head;
      while (node != null) {
        if (node.Value == Value) {
          if (node.Prev != null) {
            node.Prev.Next = node.Next;
          }
          if (node.Next != null) {
            node.Next.Prev = node.Prev;
          }
          if (node == Head) {
            Head = node.Next;
          }
          if (node == Tail) {
            Tail = node.Prev;
          }
          Length--;
          break;
        }
        node = node.Next;
      }
    }
    public int Size() {
      return Length;
    }
    public T Get(int idx) {
      Node<T> node = Head;
      while (idx > 0) {
        if (node == null) return default(T);
        node = node.Next;
        idx--;
      }
      if (node == null) return default(T);
      return node.Value;
    }
    public IEnumerator<T> GetEnumerator() {
      return new ListEnumerator<T>(this);
    }
    public Node<T> GetHead() {
      return Head;
    }
    public Node<T> GetTail() {
      return Tail;
    }
  }

  public class ListEnumerator<T> : IEnumerator<T> where T : class {
    private List<T>.Node<T> node;
    private List<T> list;
    public ListEnumerator(List<T> list) {
      this.list = list;
    }
    public bool MoveNext() {
      if (node == null) {
        node = list.GetHead();
        return true;
      }
      node = node.Next;
      return node != null;
    }
    public bool MovePrev() {
      if (node == null) return false;
      node = node.Prev;
      return node != null;
    }
    public bool HasValue() {
      return node != null;
    }
    public void Reset() {
      node = null;
    }
    public T Current {
      get {
        if (node == null) return default(T);
        return node.Value;
      }
    }
  }

  public class ListTest {
    public void Main(String[] args) {
      List<Object> listObj = new List<Object>();
      List<int> listInt = new List<int>();
    }
  }
}
