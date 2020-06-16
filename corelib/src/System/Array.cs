using System.Collections;

namespace System {
  /** C# Fixed Array Type.
   * Type casting between Array and T[] is not supported yet.
   * See Core.hpp : Core::FixedArray$T for real fixed array type.
   */
  public class Array {
    public int Length {
      get;
    }
    public static void Copy<T>(T[] src, int srcOff, T[] dst, int dstOff, int length) {
      if (src == null) return;
      if (dst == null) return;
      if (srcOff >= src.Length) {
        return;
      }
      if (dstOff >= dst.Length) {
        return;
      }
      if (srcOff + length > src.Length) {
        length = src.Length - srcOff;
      }
      if (dstOff + length > dst.Length) {
        length = dst.Length - dstOff;
      }
      for(int off=0;off<length;off++) {
        dst[dstOff + off] = src[srcOff + off];
      }
    }
  }

  /** Resizeable Array storage.
  * Fast to add, slow to remove.
  */
  public class Array<T> where T : class {
    private T[] Elements;
    private readonly int BlockSize = 1024;
    private int Blocks;
    private int Length;
    public Array() {
      Elements = new T[BlockSize];
      Blocks = 1;
      Length = 0;
    }
    public Array(int size) {
      Blocks = size / BlockSize;
      if (size % BlockSize != 0) {
        Blocks++;
      }
      Elements = new T[Blocks * BlockSize];
    }
    public Array(int size, int BlockElements) {
      BlockSize = 64;
      while (BlockSize < BlockElements) {
        BlockSize <<= 1;
      }
      Blocks = size / BlockSize;
      if (size % BlockSize != 0) {
        Blocks++;
      }
      Elements = new T[Blocks * BlockSize];
    }
    public T Get(int idx) {
      return Elements[idx];
    }
    public void Set(int idx, T value) {
      Elements[idx] = value;
    }
    public int Size() {
      return Length;
    }
    public void Add(T value) {
      if (Elements.Length == Length) {
        Grow(1);
      }
      Elements[Length] = value;
      Length++;
    }
    public void Insert(int idx, T value) {
      if (Elements.Length == Length) {
        Grow(1);
      }
      if (idx < Length) {
        Array.Copy<T>(Elements, idx, Elements, idx+1, Length - idx);
      }
      Elements[idx] = value;
      Length++;
    }
    public int IndexOf(T value) {
      for(int i=0;i<Length;i++) {
        if (Elements[i] == value) {
          return i;
        }
      }
      return -1;
    }
    public void Remove(T value) {
      int idx = IndexOf(value);
      if (idx != -1) RemoveAt(idx);
    }
    public void RemoveAt(int idx) {
      if (idx < 0 || idx >= Length) return;
      if (idx < Length-1) {
        Array.Copy<T>(Elements, idx+1, Elements, idx, Length - idx - 1);
      }
      Length--;
      //TODO : Shrink() ???
    }
    public T[] ToArray() {
      T[] copy = new T[Length];
      Array.Copy<T>(Elements, 0, copy, 0, Length);
      return copy;
    }
    public IEnumerator<T> GetEnumerator() {
      return new ArrayEnumerator<T>(this);
    }

    private void Grow(int AddBlocks) {
      Blocks += AddBlocks;
      T[] NewElements = new T[Blocks * BlockSize];
      Array.Copy<T>(Elements, 0, NewElements, 0, Length);
      Elements = NewElements;
    }
  }

  public class ArrayEnumerator<T> : IEnumerator<T> where T : class {
    public ArrayEnumerator(Array<T> array) {this.array = array;}
    private readonly Array<T> array;
    private int idx = -1;
    public bool MoveNext() {
      if (idx == array.Size()-1) return false;
      idx++;
      return true;
    }
    public T Current {
      get {
        if (idx == -1 || idx == array.Size()) return default(T);
        return array.Get(idx);
      }
    }
    public void Reset() {
      idx = -1;
    }
  }
}
