#include <stdio.h>
#include <unistd.h>
#include <pthread.h>
#include <signal.h>
#include <sys/mman.h>

#define PAGE_SIZE 0x1000

void Core::OS::ThreadGetHandle(System::Thread* thread) {
  thread->NativeHandle = (void*)pthread_self();
}

int Core::OS::GetThreadContextCount() {
  return 0;
}

void Core::OS::GetThreadContext(System::Thread *thread, void** context) {
  //not needed for Linux - signal will pushaq
}

static void cb_sig(int signal)
{
  void* local = nullptr;
  System::Thread *thread = System::Thread::Current();
  thread->StackCurrent = &local;
  switch(signal) {
  case SIGUSR1:
    //Warning : if something else wakes the thread it could corrupt the GC operation
    pause();
    break;
  case SIGUSR2:
    break;
  }
}

void Core::OS::ThreadInit(System::Thread *thread) {
  //setup signal handler (invoked inside new thread before it's added to GC thread list)
  //see https://stackoverflow.com/questions/11468333/linux-threads-suspend-resume
  struct sigaction act;
  sigemptyset(&act.sa_mask);
  act.sa_flags = 0;
  act.sa_handler = cb_sig;
  if (sigaction(SIGUSR1, &act, NULL) == -1) printf("Error:unable to handle siguser1\n");
  if (sigaction(SIGUSR2, &act, NULL) == -1) printf("Error:unable to handle siguser2\n");
}

void Core::OS::ThreadSuspend(System::Thread *thread) {
  pthread_t *p_thread = (pthread_t*)thread->NativeHandle;
  pthread_kill(*p_thread, SIGUSR1);
}

void Core::OS::ThreadResume(System::Thread *thread) {
  pthread_t *p_thread = (pthread_t*)thread->NativeHandle;
  pthread_kill(*p_thread, SIGUSR2);
}

union uptr {
  void *vptr;
  int64 v64;
};

int Core::OS::AllocateVirtualPages(int chain, int page, int cnt) {
  //mmap(void *addr, size_t length, int prot, int flags, int fd, off_t offset);
  //  prot = PROT_READ | PROT_WRITE
  //  flags = MAP_ANONYMOUS
  //  fd = -1
  //  offset = 0
  uptr ptr;
  ptr.vptr = nullptr;
  ptr.v64 = chain;
  ptr.v64 <<= 40;
  ptr.v64 += (page << 12);
  do {
    void* vptr = mmap(ptr.vptr, cnt * PAGE_SIZE, PROT_READ | PROT_WRITE, MAP_ANONYMOUS, -1, 0);
    if (vptr == ptr.vptr) return page;
    ptr.v64 += PAGE_SIZE;
    page++;
    if (page == 0x100000) {
      printf("Error:No more memory pages available for chain %d\n", chain);
      std:exit(1);
    }
  } while (true);
}
