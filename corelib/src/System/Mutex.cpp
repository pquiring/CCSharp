#include <mutex>
#include <condition_variable>

Core::Sync::Sync(System::Mutex *mutex) {
  mutex->Lock();
  this->mutex = mutex;
}

Core::Sync::~Sync() {
  mutex->Unlock();
}

void System::Mutex::Create() {
  NativeMutex = (void*)new std::mutex();
  NativeCondition = (void*)new std::condition_variable_any();
  Owner = nullptr;
  Count = 0;
}

void System::Mutex::Destroy() {
  std::mutex* mutex = (std::mutex*)NativeMutex;
  std::condition_variable_any* condition = (std::condition_variable_any*)NativeCondition;
  if (Count > 0) {
    System::Console::WriteLine(Core::utf16ToString(u"Error:~Mutex while mutex is still locked"));
  }
  delete condition;
  delete mutex;
}

void System::Mutex::Lock() {
  std::mutex* mutex = (std::mutex*)NativeMutex;
  if (Owner == System::Thread::Current()) {
    Count++;
  } else {
    mutex->lock();
    Count = 1;
    Owner = System::Thread::Current();
  }
}

void System::Mutex::Unlock() {
  std::mutex* mutex = (std::mutex*)NativeMutex;
  if (Count == 0) {
    System::Console::WriteLine(Core::utf16ToString(u"Error:Mutex unlock() but not locked"));
    return;
  }
  Count--;
  if (Count == 0) {
    Owner = nullptr;
    mutex->unlock();
  }
}

void System::Mutex::Wait() {
  std::mutex* mutex = (std::mutex*)NativeMutex;
  std::condition_variable_any* condition = (std::condition_variable_any*)NativeCondition;
  int wcount = Count;
  System::Thread* wthread = Owner;
  Count = 0;
  Owner = nullptr;
  condition->wait(*mutex);
  Count = wcount;
  Owner = wthread;
}

void System::Mutex::NotifyOne() {
  std::condition_variable_any* condition = (std::condition_variable_any*)NativeCondition;
  condition->notify_one();
}

void System::Mutex::NotifyAll() {
  std::condition_variable_any* condition = (std::condition_variable_any*)NativeCondition;
  condition->notify_all();
}
