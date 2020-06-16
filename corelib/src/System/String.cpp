namespace Core {
  System::String* utf8ToString(const char* str) {
    int len = std::strlen(str);
    FixedArray$T<uint8> *array = new(len) FixedArray$T<uint8>(&Core::Type_uint8);
    uint8*buf = array->Array;
    std::memcpy(buf, str, len);
    return new System::String(array);
  }

  static int strlen16(const char16_t*str) {
    int len = 0;
    while (*str != 0) {
      len++;
      str++;
    }
    return len;
  }

  System::String* utf16ToString(const char16_t* str) {
    int len = strlen16(str);
    FixedArray$T<char16> *array = new(len) FixedArray$T<char16>(&Core::Type_char16);
    char16*buf = array->Array;
    std::memcpy(buf, str, len*2);
    return new System::String(array);
  }

  System::String* addstr(System::String *s1, System::String *s2) {
    int len1 = s1->$get_Length();
    int len2 = s2->$get_Length();
    int len = len1 + len2;
    Core::FixedArray$T<char16> *ca = new (len) Core::FixedArray$T<char16>(&Core::Type_char16);
    System::Array::Copy$T<char16>(s1->Value, 0, ca, 0, len1);
    System::Array::Copy$T<char16>(s2->Value, 0, ca, len1, len2);
    return new System::String(ca);
  }

  //TODO : add other $addstr() functions

  System::String* addstr(System::String *s1, int64 y) {
    System::String* s2 = System::Int64::ToString(y);
    return addstr(s1, s2);
  }
}
