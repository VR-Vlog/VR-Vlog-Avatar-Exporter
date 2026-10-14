"""Native Mac resource sampling shared through OS locks, without product imports."""
import ctypes
import re
import subprocess
import sys
import time

def _darwin_cpu_percent():
    """Sample aggregate CPU time, not runnable/blocked process load average."""
    # mach/host_info.h: HOST_CPU_LOAD_INFO=3, four natural_t (uint32) ticks.
    # mach/machine.h orders them USER, SYSTEM, IDLE, NICE. All state is local
    # to this call, so concurrent schedulers never consume each other's sample.
    try:
        library = ctypes.CDLL('/usr/lib/libSystem.B.dylib')
        library.mach_host_self.argtypes = []
        library.mach_host_self.restype = ctypes.c_uint32
        library.host_statistics.argtypes = [ctypes.c_uint32, ctypes.c_int32,
                                           ctypes.POINTER(ctypes.c_int32), ctypes.POINTER(ctypes.c_uint32)]
        library.host_statistics.restype = ctypes.c_int32
        library.mach_port_deallocate.argtypes = [ctypes.c_uint32, ctypes.c_uint32]
        library.mach_port_deallocate.restype = ctypes.c_int32
        # mach_task_self() is the SDK macro for this borrowed global port.
        task = ctypes.c_uint32.in_dll(library, 'mach_task_self_').value
        if task in (0, 0xffffffff):
            raise ValueError('Cannot collect CPU load: invalid task port')
        host = library.mach_host_self()
    except (OSError, AttributeError, ValueError) as exc:
        raise ValueError('Cannot collect CPU load: Mach API unavailable') from exc
    if host in (0, 0xffffffff):
        raise ValueError('Cannot collect CPU load: invalid host port')
    try:
        def sample():
            ticks = (ctypes.c_uint32 * 4)()
            count = ctypes.c_uint32(4)
            code = library.host_statistics(host, 3, ctypes.cast(ticks, ctypes.POINTER(ctypes.c_int32)),
                                           ctypes.byref(count))
            if code != 0 or count.value != 4:
                raise ValueError('Cannot collect CPU load: incomplete Mach sample')
            return tuple(ticks)
        first = sample()
        time.sleep(0.1)
        second = sample()
        def difference(current):
            # Counter rollover is valid. A small backwards/reset sample instead
            # produces a huge unsigned delta and must not report spare capacity.
            values = [(end - start) & 0xffffffff for start, end in zip(first, current)]
            if any(value >= 0x80000000 for value in values):
                raise ValueError('Cannot collect CPU load: inconsistent Mach samples')
            return values
        delta = difference(second)
        if not sum(delta):
            # XNU rate-limits third-party host_statistics calls in one-second
            # windows and may return an identical cached sample. Retry only
            # this case once, retaining this call's original baseline.
            time.sleep(1.1)
            delta = difference(sample())
        total = sum(delta)
        if not total:
            raise ValueError('Cannot collect CPU load: inconsistent Mach samples')
        return 100.0 * (total - delta[2]) / total
    finally:
        # mach_host_self gives this call a send right; the task port is borrowed.
        if library.mach_port_deallocate(task, host) != 0:
            raise ValueError('Cannot collect CPU load: host port release failed')



def collect_metrics():
    if sys.platform != 'darwin':
        raise ValueError('Native macOS resource sampling is required')
    vm = subprocess.check_output(['/usr/bin/vm_stat'], text=True, timeout=10)
    page_size = re.search(r'page size of (\d+) bytes', vm)
    if not page_size or int(page_size[1]) <= 0:
        raise ValueError('Cannot collect memory: invalid page size')
    pages = dict(re.findall(r'^(Pages (?:free|inactive|speculative)):\s*(\d+)\.', vm, re.MULTILINE))
    if set(pages) != {'Pages free', 'Pages inactive', 'Pages speculative'}:
        raise ValueError('Cannot collect memory: incomplete vm_stat sample')
    memory = sum(int(value) for value in pages.values()) * int(page_size[1]) // (1024 * 1024)
    return {'cpu_percent': _darwin_cpu_percent(), 'memory_available_mb': memory}
