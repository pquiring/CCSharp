/** Garbage Collector
 *
 * Author : Peter Quiring
 *
 * My first attempt was very slow until I read "Garbage Collection in an Uncooperative Environment" by Hans-Juergen Boehm
 * which gave me some great ideas.
 *
 * TODO : reorganize the chain sizes into smaller objects less than 4K and only one chain for large objects that are multiples of 4K pages.
 *
 */

union uptr {
  void *vptr;
  int64 *ptr64;
  int64 v64;
  uptr() {
    vptr = nullptr;
  }
  uptr(void*in) {
    vptr = in;
  }
  uptr(int64 in) {
    v64 = in;
  }
  uptr get(int offset) {
    return (void*)ptr64[offset];
  }
  operator void*() {
    return vptr;
  }
};

struct GC_static_ref {
  Core::Object** ref;
  GC_static_ref* next;
};

static GC_static_ref* GC_static_list = nullptr;

#define PAGE_SIZE 0x1000

//#define GC_DEBUG

#ifdef GC_DEBUG
#define GC_TRACE
#endif

static int gc_last = 1;
static int gc_mark = 1;

//GC requires 64bit pointers and use of virtual addresses
#define CHAIN_MASK 0x0000ff0000000000
#define ZERO_MASK  0xffff00ff00000000
#define PAGE_MASK  0x00000000fffff000
#define OBJ_MASK   0x0000000000000fff

//Windows : max virtual addr = 0x0020000000000000 = 8 TB
//Linux : max virtual addr = untested

static void* make_ptr(int chain, int page) {
  uptr ptr;
  ptr.vptr = nullptr;
  ptr.v64 = chain + 1;
  ptr.v64 <<= 40;
  ptr.v64 += (page << 12);
  return ptr.vptr;
}

static void* make_ptr(int chain, int page, int offset) {
  uptr ptr;
  ptr.vptr = nullptr;
  ptr.v64 = chain + 1;
  ptr.v64 <<= 40;
  ptr.v64 += (page << 12);
  ptr.v64 += offset;
  return ptr.vptr;
}

/** Block points to one 4K page of memory that contains objects of a fixed size (power of 2). */
struct Block {
  int size;  //size of each object
  int count;  //# of objects in block
  int count_free;  //# of objects free in block
  int count_ptrs;  //# of pointers per object (size / 8)
  int page_first;
  int page_last;
  Block* next;
  int *marks;

  void init(int size, int count, int page_first, int page_last) {
    this->size = size;
    this->count = count;
    this->count_free = count;
    this->count_ptrs = size / 8;
    this->next = nullptr;
    this->marks = new int[count];
    this->page_first = page_first;
    this->page_last = page_last;
    std::memset(marks, 0, sizeof(int) * count);
  }
};

#define NUM_CHAINS 24
#define MAX_SIZE (256 * 1024 * 1024)

Block *block_chains[NUM_CHAINS];
//32,64,128,256,512,1k,2k,4k  //small objects (single page)
//8k,16k,32k,64k,128k,256k,512k,1M  //large objects (multiple pages)
//2M,4M,8M,16M,32M,64M,128M,256M  //extra large objects (same as large)

static System::Mutex *gc_lock;
static System::Mutex *gc_lock2;
static System::Thread *gc_thread = nullptr;
static System::Thread *main_thread = nullptr;

static void GC_reclaim_locked();

static uptr *gc_context;
static int gc_context_count;

static bool active = false;
static bool doReclaim = false;

#ifdef GC_DEBUG
int64 t_suspend;
int64 t_mark;
int64 t_delete;
int64 t_regs;
int64 t_stack;
#endif

static System::Thread *thread_list = nullptr;

struct GCThread : public System::Thread {
  void Run() override {
    gc_lock->Lock();
    gc_lock2->Lock();
    gc_lock2->NotifyAll();  //signal main thread that GC thread is running
    gc_lock2->Unlock();
    active = true;
    while (active) {
      gc_lock->Wait();
      if (doReclaim) {
        doReclaim = false;
        gc_lock2->Lock();
        GC_reclaim_locked();
        gc_lock2->NotifyAll();
        gc_lock2->Unlock();
      }
    }
    gc_lock->Unlock();
  }
};

static void GC_reclaim_signal() {
  gc_lock2->Lock();
  doReclaim = true;
  gc_lock->NotifyAll();  //notify gc to reclaim memory
  gc_lock->Unlock();
  gc_lock2->Wait();  //wait for gc to complete
  gc_lock2->Unlock();
  gc_lock->Lock();
}

void System::Environment::Collect() {
  gc_lock->Lock();
  GC_reclaim_signal();
  gc_lock->Unlock();
}

static void* GC_malloc_locked(int chain) {
  Block *blk = block_chains[chain];
  if (blk == nullptr) return nullptr;
  while (blk != nullptr) {
    if (blk->count_free > 0) {
      int *marks = blk->marks;
      int count = blk->count;
      for(int idx=0;idx<count;idx++) {
        if (marks[idx] == 0) {
          marks[idx] = gc_mark;
          blk->count_free--;
          void* ptr = make_ptr(chain, blk->page_first, idx * blk->size);
          std::memset(ptr, 0, blk->size);
          return ptr;
        }
      }
    }
    blk = blk->next;
  }
  return nullptr;
}

static void GC_add_block(int size, int chain) {
  int page = 0;
  Block *lastblk = block_chains[chain];
  if (lastblk != nullptr) {
    page = lastblk->page_last + 1;
  }
  Block *newblk = new Block();
  int pages = size / PAGE_SIZE;
  if (pages < 1) pages = 1;
  int page_first = Core::OS::AllocateVirtualPages(chain + 1, page, pages);
  int page_last = page_first + pages - 1;
  newblk->init(size, (pages * PAGE_SIZE) / size, page_first, page_last);
  newblk->next = lastblk;
  block_chains[chain] = newblk;
}

static bool GC_inited = false;

void Core::Object::GC_init(void *main_stack) {
  std::memset(block_chains, 0, sizeof(Block*) * NUM_CHAINS);

  main_thread = new System::Thread();
  main_thread->StackStart = main_stack;
  thread_list = main_thread;
  main_thread->GC_setup_main_thread();  //setup current_thread
  Core::OS::ThreadGetHandle(main_thread);  //setup NativeHandle

  gc_context_count = Core::OS::GetThreadContextCount();
  gc_context = new uptr[gc_context_count];
  gc_lock = new System::Mutex();
  gc_lock2 = new System::Mutex();
  GC_inited = true;
  Core::Object::GC_add_static_ref((Core::Object**)&gc_thread);
  gc_thread = new GCThread();  //this will invoke GC_malloc()
#ifdef GC_TRACE
  printf("%p gc_thread\n", gc_thread);
#endif
  gc_lock2->Lock();
  gc_thread->Start();
  gc_lock2->Wait();  //wait for gc_thread to start
  gc_lock2->Unlock();
}

static void GC_uninit() {
  active = false;
  gc_lock->Lock();
  gc_lock->NotifyAll();
  gc_lock->Unlock();
}

void* Core::Object::GC_malloc(int size) {
  if (!GC_inited) {
    return malloc(size);
  }
  //align size to power of 2
  if (size > MAX_SIZE) {
    printf("Fatal Error:GC_mallc() size > MAX_SIZE\n");
    std::exit(1);
  }
  int p2size = 32;  //smallest size
  int chain = 0;
  while (p2size < size) {
    p2size <<= 1;
    chain++;
  }
  size = p2size;
  gc_lock->Lock();
  if (block_chains[chain] == nullptr) {
    GC_add_block(size, chain);
  }
  void* blk = GC_malloc_locked(chain);
  if (blk != nullptr) {
    gc_lock->Unlock();
    return blk;
  }
  //no free memory found : try to reclaim some unused memory
  if (active) {
    GC_reclaim_signal();
    blk = GC_malloc_locked(chain);
    if (blk != nullptr) {
      gc_lock->Unlock();
      return blk;
    }
  }
  //still not available - add a new block
  GC_add_block(size, chain);
  blk = GC_malloc_locked(chain);
  if (blk != nullptr) {
    gc_lock->Unlock();
    return blk;
  }
  //Error : should not get here
  printf("Fatal Error:GC_malloc() failed\n");
  std::exit(1);
  return nullptr;
}

void System::Thread::GC_add_thread() {
#ifdef GC_TRACE
  printf("%p GC_add_thread\n", this);
#endif
  gc_lock->Lock();
  Prev = thread_list;
  this->Next = thread_list;
  thread_list = this;
  Core::OS::ThreadInit(this);
  gc_lock->Unlock();
}

void System::Thread::GC_delete_thread() {
  gc_lock->Lock();
  if (Prev != nullptr) {
    Prev->Next = Next;
  }
  if (Next != nullptr) {
    Next->Prev = Prev;
  }
  if (thread_list == this) {
    thread_list = Prev;
  }
  gc_lock->Unlock();
}

#ifdef GC_DEBUG
int64 blocks;
int64 blocksSize;
int64 freed;
int64 freedSize;
int64 marked;
int64 markedSize;
#endif

//mark object and everything it references recursively
void GC_mark_block(uptr ptr, int lvl) {
  uptr zero = ptr.v64 & ZERO_MASK;
  if (zero != nullptr) return;
  uptr chainptr = ptr.v64 & CHAIN_MASK;
  if (chainptr == nullptr) return;
  int chain = (int)(chainptr.v64 >> 40) - 1;
  if (chain >= NUM_CHAINS) return;
  int page = (int)((ptr.v64 & PAGE_MASK) >> 12);

  Block *blk = block_chains[chain];
  while (blk != nullptr) {
    if (page >= blk->page_first && page <= blk->page_last) {
      //ptr is a valid object reference
      Core::Object* objptr = (Core::Object*)ptr.vptr;
      if (chain < 8) {
        //small object : size <= page
        int object = (int)((ptr.v64 & OBJ_MASK) >> (chain + 5));
        if (blk->marks[object] == gc_last) {
          blk->marks[object] = gc_mark;
          //now check sub-references
          if (objptr->GC_flags & Core::GC_PA) return;  //primitive array : do not scan
          uptr subptr = make_ptr(chain, page);
          subptr.v64 += object * blk->size;
          for(int a=0;a<blk->count_ptrs;a++) {
            GC_mark_block(subptr.get(a), lvl++);
          }
        }
      } else {
        //large object (multiple pages)
        if (blk->marks[0] == gc_last) {
          blk->marks[0] = gc_mark;
          //now check sub-references (large)
          if (objptr->GC_flags & Core::GC_PA) return;  //primitive array : do not scan
          uptr subptr = make_ptr(chain, page);
          for(int a=0;a<blk->count_ptrs;a++) {
            GC_mark_block(subptr.get(a), lvl++);
          }
        }
      }
#ifdef GC_DEBUG
      marked++;
      markedSize += blk->size;
#endif
      break;
    }
    blk = blk->next;
  }
}

static void GC_mark_static_list() {
  GC_static_ref *ref = GC_static_list;
  while (ref != nullptr) {
#ifdef GC_TRACE
    printf("%p static\n", *ref->ref);
#endif
    GC_mark_block(*ref->ref, 1);
    ref = ref->next;
  }
}

static void GC_mark_thread_list() {
  System::Thread *ref = thread_list;
  while (ref != nullptr) {
#ifdef GC_TRACE
    printf("%p thread mark\n", ref);
#endif
    GC_mark_block(ref, 1);
    ref = ref->Next;
  }
}

static void GC_reclaim_locked() {
#ifdef GC_DEBUG
  int64 start, end;
  blocks = 0;
  blocksSize = 0;
  freed = 0;
  freedSize = 0;
  marked = 0;
  markedSize = 0;
#endif
#ifdef GC_TRACE
  printf("%p GC_reclaim\n", System::Thread::Current());
#endif
  gc_last = gc_mark;
  gc_mark++;
  if (gc_mark == 0x7fffffff) gc_mark = 1;
  //mark static fields using GC_static_list
#ifdef GC_DEBUG
  start = System::DateTime::CurrentTimeEpoch();
#endif
  GC_mark_static_list();
  GC_mark_thread_list();
  //stop all threads
#ifdef GC_DEBUG
  end = System::DateTime::CurrentTimeEpoch();
  t_mark = end - start;
  start = System::DateTime::CurrentTimeEpoch();
#endif
  System::Thread *thread = thread_list;
  while (thread != nullptr) {
    if (thread != gc_thread) {
#ifdef GC_TRACE
      printf("%p suspend\n", thread);
#endif
      Core::OS::ThreadSuspend(thread);
    }
    thread = thread->Next;
  }
  //scan all threads for references
  thread = thread_list;
  while (thread != nullptr) {
    if (thread != gc_thread) {
#ifdef GC_TRACE
      printf("%p get context\n", thread);
#endif
      //get thread context (registers)
      Core::OS::GetThreadContext(thread, (void**)gc_context);
#ifdef GC_TRACE
      printf("%p thread stack : %p - %p\n", thread, thread->StackStart, thread->StackCurrent);
#endif
#ifdef GC_DEBUG
      start = System::DateTime::CurrentTimeEpoch();
#endif
      for(int a=0;a<gc_context_count;a++) {
        uptr ptr = gc_context[a];
        GC_mark_block(ptr, 0);
      }
#ifdef GC_DEBUG
      end = System::DateTime::CurrentTimeEpoch();
      t_regs = end - start;
      start = System::DateTime::CurrentTimeEpoch();
#endif
      //check thread stack
      uptr StackStart = thread->StackStart;
      if (StackStart.vptr == nullptr) continue;  //not running yet
      uptr stack_current = thread->StackCurrent;
      if (stack_current == nullptr) {
#ifdef GC_TRACE
        printf("%p : thread invalid stack current\n", thread);
#endif
        continue;
      }
      int pos = 0;
      while (stack_current.vptr < StackStart.vptr) {
        uptr ptr = stack_current.get(0);
        GC_mark_block(ptr, 0);
        stack_current.ptr64++;
      }
    }
    thread = thread->Next;
  }
#ifdef GC_DEBUG
  end = System::DateTime::CurrentTimeEpoch();
  t_stack = end - start;
#endif
  //resume all threads
  thread = thread_list;
  while (thread != nullptr) {
    if (thread != gc_thread) {
      Core::OS::ThreadResume(thread);
    }
    thread = thread->Next;
  }
#ifdef GC_DEBUG
  end = System::DateTime::CurrentTimeEpoch();
  t_suspend = end - start;
  start = System::DateTime::CurrentTimeEpoch();
#endif
  //now walk through objects and delete any without current mark
  for(int chain=0;chain<NUM_CHAINS;chain++) {
    Block *blk = block_chains[chain];
    while (blk != nullptr) {
#ifdef GC_DEBUG
      blocks += blk->count;
      blocksSize += blk->count * blk->size;
#endif
      for(int idx=0;idx < blk->count;idx++) {
        int mark = blk->marks[idx];
        if (mark != 0 && mark != gc_mark) {
        //delete block
#ifdef GC_DEBUG
          freed++;
          freedSize += blk->size;
#endif
          Core::Object *obj = (Core::Object*)make_ptr(chain, blk->page_first, idx * blk->size);
#ifdef GC_TRACE
          printf("%p delete it\n", obj);
#endif
          delete obj;
          blk->marks[idx] = 0;
          blk->count_free++;
        }
      }
      blk = blk->next;
    }
  }
#ifdef GC_DEBUG
  end = System::DateTime::CurrentTimeEpoch();
  t_delete = end - start;
  printf("gc:blks=%lld size=%lld free=%lld size=%lld mark=%lld size=%lld\n"
    , blocks, blocksSize, freed, freedSize, marked, markedSize);
  printf("gc:chains:");
  for(int chain=0;chain<NUM_CHAINS;chain++) {
    int blk_count = 0;
    int obj_count = 0;
    int obj_size = 0;
    Block *blk = block_chains[chain];
    while (blk != nullptr) {
      blk_count++;
      obj_count += blk->count;
      obj_size = blk->size;
      blk = blk->next;
    }
    if (blk_count > 0) {
      printf("%d %d %d %d:", chain, obj_size, blk_count, obj_count);
    }
  }
  printf("\n");
  printf("gc:ms suspend=%lld mark=%lld delete=%lld regs=%lld stack=%lld\n", t_suspend, t_mark, t_delete, t_regs, t_stack);
#endif
}

//TODO : static_list should be PER library so they can be unloaded
void Core::Object::GC_add_static_ref(Core::Object** ref) {
  GC_static_ref* field = new GC_static_ref();
  field->ref = ref;
  field->next = GC_static_list;
  GC_static_list = field;
}

void* Core::Object::operator new(std::size_t size) {
  return GC_malloc(size);
}

void Core::Object::operator delete(void* ptr) {
  //do nothing
}

Core::Object::~Object() {}

namespace Core {

  int g_argc;
  const char** g_argv;

  Class Class_Core_Object(false, "Object", nullptr, {}, {}, {}, nullptr);

  Class Class_int8("sbyte");
  Class Class_int16("short");
  Class Class_int32("int");
  Class Class_int64("long");

  Class Class_uint8("byte");
  Class Class_uint16("ushort");
  Class Class_uint32("uint");
  Class Class_uint64("ulong");

  Class Class_char16("char");
  Class Class_bool("bool");
  Class Class_void("void");

  System::Type Type_int8(&Class_int8);
  System::Type Type_int16(&Class_int16);
  System::Type Type_int32(&Class_int32);
  System::Type Type_int64(&Class_int64);
  System::Type Type_uint8(&Class_int8);
  System::Type Type_uint16(&Class_int16);
  System::Type Type_uint32(&Class_int32);
  System::Type Type_uint64(&Class_int64);
  System::Type Type_char16(&Class_char16);
  System::Type Type_bool(&Class_bool);
}
