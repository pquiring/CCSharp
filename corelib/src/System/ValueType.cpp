void* System::ValueType::GetPointer() {
  uint8* ptr = (uint8*)this;
  ptr += sizeof(System::ValueType);  //8 bytes just for the vtable pointer
  return (void*)ptr;
}
