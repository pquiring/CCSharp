namespace System {
  public class String {
    private char[] Value;
    public int Length {
      get {return Value.Length;}
    }
    public String() {
      Value = new char[0];
    }
    public String(String str) {
      int len = str.Length;
      Value = new char[len];
      Array.Copy<char>(str.Value, 0, Value, 0, len);
    }
    public String(String str, int offset, int length = -1) {
      int thisLength = str.Length;
      if (length == -1) {
        length = thisLength - offset;
      }
      if (offset + length > thisLength) {
        length = thisLength - offset;
      }
      Value = new char[length];
      Array.Copy<char>(str.Value, offset, Value, 0, length);
    }
    public String(char[] chars, int offset = 0, int length = -1) {
      if (offset >= chars.Length) {
        offset = 0;
        length = 0;
      }
      int len = length == -1 ? chars.Length : length;
      if (offset + len >= chars.Length) {
        len = chars.Length - offset;
      }
      Value = new char[len];
      Array.Copy<char>(chars, offset, Value, 0, len);
    }
    public String(byte[] utf8) {
      /**
        UTF8 Format:
          1st bytes:
            0xxxxxxx = 7bit (1 byte)
            110xxxxx = 5bit (2 byte) 11bit total (import supported) (not exported)
            1110xxxx = 4bit (3 byte) 16bit total (import/export supported)
            11110xxx = 3bit (4 byte) 21bit total (import truncated to 16bit) (not exported)
          2nd,3rd,4th bytes only:
            10xxxxxx = 6bit
      */
      //calc # utf8 codes from utf8
      int pos = 0;
      int length = 0;
      for(int a=0;a<utf8.Length;a++) {
        byte ch = utf8[a];
        if (ch > 127) {
          length++;
          if ((ch & 0b11110000) == 0b11110000) {
            //21 bit - 4 bytes - not supported (truncate to 16bit)
            a += 3;
          } else if ((ch & 0b11100000) == 0b11100000) {
            //16 bit - 3 bytes
            a += 2;
          } else if ((ch & 0b11000000) == 0b11000000) {
            //11 bit - 2 bytes
            a++;
          } else {
            //bad utf8 code (just output 6 bit code)
          }
        } else {
          length++;
        }
      }

      //convert codes
      Value = new char[length];
      for(int a=0;a<length;a++) {
        int ch = utf8[pos++];
        int bits;
        int bits32;
        if (ch > 127) {
          if ((ch & 0b11110000) == 0b11110000) {
            //21 bit - 4 bytes - not supported (truncate to 16bit)
            if (a+3 > length) break;
            //3 6 6 6 = 21 bits
            bits32 = (ch & 0b00000111) << 18;
            ch = utf8[pos++];
            bits32 |= (ch & 0b00111111) << 12;
            ch = utf8[pos++];
            bits32 |= (ch & 0b00111111) << 6;
            ch = utf8[pos++];
            bits32 |= (ch & 0b00111111);
            ch = (bits32 & 0xffff);  //bits lost
          } else if ((ch & 0b11100000) == 0b11100000) {
            //16 bit - 3 bytes
            if (a+2 > length) break;
            //4 6 6
            bits = (ch & 0b00001111) << 12;
            ch = utf8[pos++];
            bits |= (ch & 0b00111111) << 6;
            ch = utf8[pos++];
            bits |= (ch & 0b00111111);
            ch = bits;
          } else if ((ch & 0b11000000) == 0b11000000) {
            //11 bit - 2 bytes
            if (a+1 > length) break;
            //5 6
            bits = (ch & 0b00011111) << 6;
            ch = utf8[pos++];
            bits |= (ch & 0b00111111);
            ch = bits;
          } else {
            //bad utf8 code (just output 6 bit code)
            ch &= 0b00111111;
          }
        }
        Value[a] = (char)ch;
      }
    }
    public byte[] ToByteArray() {
      //calc # utf8 bytes from utf16
      int length = Value.Length;
      int bytes = length;
      for(int a=0;a<length;a++) {
        if (Value[a] > 127) bytes += 2;
      }
      byte[] copy = new byte[bytes];
      //convert codes
      int pos = 0;
      for(int a=0;a<length;a++) {
        char ch = Value[a];
        if (ch > 127) {
          //3 byte format (16 bit)
          //1110xxxx = 4bit (3 byte) 16bit total
          //10xxxxxx = 6bit
          //10xxxxxx = 6bit
          copy[pos++] = (byte)(0b1110000 + (ch >> 12));
          copy[pos++] = (byte)(0b1000000 + ((ch >> 6) & 0b00111111));
          copy[pos++] = (byte)(0b1000000 + (ch & 0b00111111));
        } else {
          copy[pos++] = (byte)ch;
        }
      }
      return copy;
    }
    public char[] ToCharArray() {
      int length = Length;
      char[] copy = new char[length];
      Array.Copy<char>(Value, 0, copy, 0, length);
      return copy;
    }
    public String Substring(int offset, int length = -1) {
      if (length == -1) {
        length = Value.Length - offset;
      }
      if (length < 0) {
        return new String();
      }
      return new String(this, offset, length);
    }
    public int IndexOf(String str, int offset = 0, int length = -1) {
      int thisLength = length == -1 ? Value.Length - offset : length;
      if (thisLength <= 0) return -1;
      int strLength = str.Value.Length;
      if (strLength > thisLength) return -1;
      char[] cmp = str.Value;
      int end = thisLength - strLength + offset;
      for(int i=offset;i<end;i++) {
        bool match = true;
        for(int len=0;len<strLength;len++) {
          if (Value[i+len] != cmp[len]) {
            match = false;
            break;
          }
        }
        if (match) return i;
      }
      return -1;
    }
    public int IndexOf(char ch, int offset = 0, int length = -1) {
      int thisLength = length == -1 ? Value.Length - offset : length;
      if (thisLength <= 0) return -1;
      int end = offset + thisLength;
      for(int i=offset;i<end;i++) {
        if (Value[i] == ch) return i;
      }
      return -1;
    }
    public String[] Split(String token) {
      String[] strs;
      int thisLength = Length;
      int tokenLength = token.Length;
      if (tokenLength == 0 || tokenLength >= thisLength) {
        strs = new String[1];
        strs[0] = this;
        return strs;
      }
      int cnt = 1;
      int off = 0;
      while (off < thisLength) {
        int idx = IndexOf(token, off);
        if (idx == -1) break;
        cnt++;
        off = idx + tokenLength;
      }
      strs = new String[cnt];
      cnt = 0;
      off = 0;
      while (off < thisLength) {
        int idx = IndexOf(token, off);
        if (idx == -1) {
          strs[cnt] = Substring(off);
          break;
        } else {
          strs[cnt] = Substring(off, idx - off);
        }
        cnt++;
        off = idx + tokenLength;
      }
      return strs;
    }
    public String Join(String[] parts) {
      int totalLength = 0;
      for(int i=0;i<parts.Length;i++) {
        totalLength += parts[i].Value.Length;
      }
      char[] tmp = new char[totalLength];
      int offset = 0;
      for(int i=0;i<parts.Length;i++) {
        Array.Copy<char>(parts[i].Value, 0, tmp, offset, parts[i].Value.Length);
      }
      return new String(tmp);
    }
    public bool Equals(String other) {
      int length = Value.Length;
      if (other.Value.Length != length) return false;
      char[] s1 = Value;
      char[] s2 = other.Value;
      for(int i=0;i<length;i++) {
        if (s1[i] != s2[i]) return false;
      }
      return true;
    }
    public bool Contains(String str) {
      return IndexOf(str) != -1;
    }
  }
}
