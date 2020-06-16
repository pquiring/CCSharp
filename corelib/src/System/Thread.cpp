#include <thread>

thread_local System::Thread *current_thread;

static void start_thread(System::Thread *thread) {
  void* local = nullptr;
  thread->StackStart = (void*)&local;
  current_thread = thread;
  Core::OS::ThreadGetHandle(thread);
  thread->GC_add_thread();
  try {
    thread->Run();
  } catch (...) {
    printf("Thread:uncaught exception, thread terminated\n");
  }
  thread->GC_delete_thread();
}

void System::Thread::GC_setup_main_thread() {
  current_thread = this;
}

void System::Thread::Create() {
  std::thread * std_thread = new std::thread(start_thread, this);
  StdThread = (void*)std_thread;
}

void System::Thread::Join() {
  std::thread *std_thread = (std::thread*)StdThread;
  if (std_thread != nullptr) {
    std_thread->join();
  }
}

void System::Thread::Destroy() {
  std::thread *std_thread = (std::thread*)StdThread;
  if (std_thread != nullptr) {
    delete std_thread;
  }
}

System::Thread* System::Thread::Current() {
  return current_thread;
}
