"""Stop dedicated CI work, without treating verified Apple helpers as CI work.

The root caller owns one once-runner per UID, captures its session leader before
wait(), and calls stop_and_verify only after wait(). UID isolation also catches
children which daemonize into another process group. No killpg on reaped PIDs.
Kernel queries use public libproc and Apple's csops STATUS (read-only) ABI.
"""
import ctypes
import errno
import os
from pathlib import Path
import signal
import stat
import time

UIDS = frozenset((402,))
AUXILIARIES = frozenset(('/usr/sbin/distnoted', '/usr/sbin/cfprefsd'))
REQUIRED_FLAGS = 0x00000001 | 0x20000000 | 0x04000000  # VALID, SIGNED, PLATFORM_BINARY
REFUSED_FLAGS = 0x00000002 | 0x10000000  # ADHOC, DEBUGGED
IDENTITY_KEYS = {'pid', 'uid', 'pgid', 'start_sec', 'start_usec'}


class UnknownProcesses(RuntimeError):
    """Keep the job reservation; never print raw process metadata."""


class Gone(Exception):
    pass


class BSDInfo(ctypes.Structure):
    _fields_ = [(name, ctypes.c_uint32) for name in (
        'flags', 'status', 'xstatus', 'pid', 'ppid', 'uid', 'gid',
        'ruid', 'rgid', 'svuid', 'svgid', 'reserved')]
    _fields_ += [('comm', ctypes.c_char * 16), ('name', ctypes.c_char * 32)]
    _fields_ += [(name, ctypes.c_uint32) for name in ('nfiles', 'pgid', 'jobc', 'tdev', 'tpgid')]
    _fields_ += [('nice', ctypes.c_int32), ('start_sec', ctypes.c_uint64), ('start_usec', ctypes.c_uint64)]


def require_scope(uid):
    if type(uid) is not int or uid not in UIDS or os.geteuid() != 0:
        raise UnknownProcesses('CI_PROCESS_SCOPE')


def same_image(before, after, first, last, flags, protected):
    return (before == after and first == last and first in AUXILIARIES
            and protected and flags & REQUIRED_FLAGS == REQUIRED_FLAGS
            and not flags & REFUSED_FLAGS)


class Backend:
    def __init__(self):
        if ctypes.sizeof(BSDInfo) != 136:
            raise UnknownProcesses('PROCESS_ABI')
        self.proc = ctypes.CDLL('/usr/lib/libproc.dylib', use_errno=True)
        self.system = ctypes.CDLL('/usr/lib/libSystem.B.dylib', use_errno=True)
        for name, args in (
                ('proc_listpids', [ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_int]),
                ('proc_pidinfo', [ctypes.c_int, ctypes.c_int, ctypes.c_uint64, ctypes.c_void_p, ctypes.c_int]),
                ('proc_pidpath', [ctypes.c_int, ctypes.c_void_p, ctypes.c_uint32])):
            function = getattr(self.proc, name)
            function.argtypes, function.restype = args, ctypes.c_int
        self.system.csops.argtypes = [ctypes.c_int, ctypes.c_uint, ctypes.c_void_p, ctypes.c_size_t]
        self.system.csops.restype = ctypes.c_int

    def info(self, pid):
        value = BSDInfo()
        ctypes.set_errno(0)
        got = self.proc.proc_pidinfo(pid, 3, 0, ctypes.byref(value), ctypes.sizeof(value))
        if got != ctypes.sizeof(value):
            if ctypes.get_errno() == errno.ESRCH:
                raise Gone()
            raise UnknownProcesses('PROCESS_INFO')
        return {key: getattr(value, key) for key in IDENTITY_KEYS}

    def path(self, pid):
        value = ctypes.create_string_buffer(4096)
        ctypes.set_errno(0)
        if self.proc.proc_pidpath(pid, value, len(value)) <= 0:
            if ctypes.get_errno() == errno.ESRCH:
                raise Gone()
            raise UnknownProcesses('PROCESS_PATH')
        return value.value.decode('utf-8', errors='strict')

    def classify(self, pid, uid):
        before = self.info(pid)
        if before['uid'] != uid or before['pid'] != pid:
            raise UnknownProcesses('PROCESS_IDENTITY')
        first = self.path(pid)
        auxiliary = False
        if first in AUXILIARIES:
            value = ctypes.c_uint32()
            ctypes.set_errno(0)
            if self.system.csops(pid, 0, ctypes.byref(value), ctypes.sizeof(value)) != 0:
                if ctypes.get_errno() == errno.ESRCH:
                    raise Gone()
                raise UnknownProcesses('PROCESS_SIGNATURE')
            file = Path(first)
            entries = [file.lstat(), *(parent.lstat() for parent in file.parents)]
            protected = (stat.S_ISREG(entries[0].st_mode)
                         and all(s.st_uid == 0 and not s.st_mode & 0o022
                                 and not stat.S_ISLNK(s.st_mode) for s in entries))
            auxiliary = same_image(before, self.info(pid), first, self.path(pid), value.value, protected)
            if not auxiliary:
                raise UnknownProcesses('APPLE_IMAGE_UNCONFIRMED')
        if before != self.info(pid) or first != self.path(pid):
            raise UnknownProcesses('PROCESS_CHANGED')
        return dict(before, auxiliary=auxiliary)

    def pids(self, uid):
        values = (ctypes.c_int * 16384)()
        ctypes.set_errno(0)
        got = self.proc.proc_listpids(4, uid, values, ctypes.sizeof(values))
        if (got < 0 or (got == 0 and ctypes.get_errno()) or got >= ctypes.sizeof(values)
                or got % ctypes.sizeof(ctypes.c_int)):
            raise UnknownProcesses('PROCESS_ENUMERATION')
        pids = [pid for pid in values[:got // ctypes.sizeof(ctypes.c_int)] if pid > 0]
        if len(pids) != len(set(pids)):
            raise UnknownProcesses('PROCESS_ENUMERATION')
        return set(pids)

    def snapshot(self, uid):
        for _ in range(3):
            rows = []
            for pid in self.pids(uid):
                try:
                    rows.append(self.classify(pid, uid))
                except Gone:
                    pass  # Positive exit, not permission failure.
            if self.pids(uid) == {row['pid'] for row in rows}:
                return rows
        raise UnknownProcesses('PROCESS_INVENTORY_MOVING')

    def terminate(self, row, sig):
        try:
            current = self.classify(row['pid'], row['uid'])
            if current != row:
                raise UnknownProcesses('PROCESS_CHANGED_BEFORE_SIGNAL')
            if current['auxiliary']:
                raise UnknownProcesses('APPLE_HELPER_SIGNAL_REFUSED')
            os.kill(row['pid'], sig)
        except (Gone, ProcessLookupError):
            pass


def capture_process(pid, uid, *, backend=None):
    require_scope(uid)
    backend = backend or Backend()
    try:
        row = backend.classify(pid, uid)
    except Gone:
        raise UnknownProcesses('ONCE_LEADER_GONE') from None
    if row['auxiliary'] or row['pgid'] != pid:
        raise UnknownProcesses('ONCE_SESSION_LEADER')
    return {key: row[key] for key in IDENTITY_KEYS}


def verify_uid_idle(uid, *, backend=None):
    require_scope(uid)
    rows = (backend or Backend()).snapshot(uid)
    if any(not row['auxiliary'] for row in rows):
        raise UnknownProcesses('CI_PROCESSES_REMAIN')
    return {'ci_stopped': True, 'auxiliary_count': len(rows)}


def stop_and_verify(captured, uid, *, backend=None, clock=time.monotonic, sleep=time.sleep):
    require_scope(uid)
    if (not isinstance(captured, dict) or set(captured) != IDENTITY_KEYS
            or any(type(x) is not int or x < 0 for x in captured.values())
            or captured['uid'] != uid or captured['pid'] <= 1 or captured['pgid'] != captured['pid']):
        raise UnknownProcesses('ONCE_CAPTURE')
    backend = backend or Backend()
    # Parent wait() must precede this call. A live/reused leader means no stop is authorized.
    try:
        backend.info(captured['pid'])
    except Gone:
        pass
    else:
        raise UnknownProcesses('ONCE_LEADER_NOT_REAPED')
    for sig in (signal.SIGTERM, signal.SIGKILL):
        deadline = clock() + 10
        while True:
            rows = backend.snapshot(uid)  # Classify all before sending any signal.
            work = [row for row in rows if not row['auxiliary']]
            if not work:
                return {'ci_stopped': True, 'auxiliary_count': len(rows)}
            for row in work:
                backend.terminate(row, sig)
            if clock() >= deadline:
                break
            sleep(0.2)
    return verify_uid_idle(uid, backend=backend)
