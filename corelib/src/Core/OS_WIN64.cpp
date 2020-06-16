#include <thread>

#define PAGE_SIZE 0x1000

union uptr {
  void *vptr;
  int64 v64;
};

#undef int64
#define int64 w__int64

#define _AMD64_
#include <processthreadsapi.h>
#include <windows.h>

#undef int64
#define int64 long long

void Core::OS::ThreadGetHandle(System::Thread* thread) {
  //NOTE : GetCurrentThread() returns a pseudo thread that is ALWAYS the current thread
  //Must call OpenThread() or DuplicateThread()
  thread->NativeHandle = OpenThread(READ_CONTROL | THREAD_GET_CONTEXT, false, GetCurrentThreadId());
}

int Core::OS::GetThreadContextCount() {
  return 16;  //RAX thru R15
}

// see https://docs.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-context
static CONTEXT thread_context;

void Core::OS::GetThreadContext(System::Thread *thread, void** context) {
  thread_context.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER;
  HANDLE handle = (HANDLE)thread->NativeHandle;
  if (!::GetThreadContext(handle, &thread_context)) {
    printf("Error:GetThreadContext() failed : %x\n", GetLastError());
    std::exit(1);
  }
  std::memcpy((void*)context, &thread_context.Rax, 16 * sizeof(void*));  //RAX thru R15
  uptr rsp;
  rsp.v64 = (int64)thread_context.Rsp;
  thread->StackCurrent = rsp.vptr;
}

void Core::OS::ThreadInit(System::Thread *thread) {
  //nothing needed for Win64
}

void Core::OS::ThreadSuspend(System::Thread *thread) {
  HANDLE handle = (HANDLE)thread->NativeHandle;
  SuspendThread(handle);
}

void Core::OS::ThreadResume(System::Thread *thread) {
  HANDLE handle = (HANDLE)thread->NativeHandle;
  ResumeThread(handle);
}

int Core::OS::AllocateVirtualPages(int chain, int page, int cnt) {
  uptr ptr;
  ptr.vptr = nullptr;
  ptr.v64 = chain;
  ptr.v64 <<= 40;
  ptr.v64 += (page << 12);
  do {
    void* vptr = VirtualAlloc(ptr.vptr, cnt * PAGE_SIZE, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (vptr != nullptr) return page;
    ptr.v64 += PAGE_SIZE;
    page++;
    if (page == 0x100000) {
      printf("Error:No more memory pages available for chain %d\n", chain);
      std:exit(1);
    }
  } while (true);
}
