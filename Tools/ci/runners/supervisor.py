"""Exporter-owned root supervisor for the existing ephemeral GitHub runner.

Only this service uses the existing owner's controller API session. Each job
keeps its dedicated UID 402 and disposable registration, HOME and filesystem.
Legacy filesystem/service names are compatibility aliases, not mySI imports.
"""
import argparse
import ctypes
import fcntl
import grp
import json
import os
from pathlib import Path
import pwd
import re
import shutil
import socket
import stat
import subprocess
import sys
import tarfile
import threading
import time
import uuid

import ci_processes
from policy import SEATS, authorize, positive
from resources import collect_metrics

ROOT = Path('/Library/Application Support/mySI-CI')
RUNTIME = Path('/opt/mySI-CI')
CONTROLLER_GH = Path('/opt/LocalGitHubRunners/tools/gh')
DOTNET_GROUPS = RUNTIME / 'home/Library/Group Containers'
DISK_FLOOR = 20 * 1024 ** 3
ALLOCATION_MARGIN = 64 * 1024 ** 2
ADMISSION_WAIT_SECONDS = 3600


def controller_executable():
    executable = protected(CONTROLLER_GH)
    if not executable.is_file() or not os.access(executable, os.X_OK):
        raise ValueError('CI-owned GitHub CLI is unavailable')
    return executable


def processes(uid):
    ci_processes.require_scope(uid)
    return [row['pid'] for row in ci_processes.Backend().snapshot(uid) if not row['auxiliary']]


def stop_owned_processes(uid):
    # Recovery does not own a captured/reaped listener. Preserve any active
    # work for an explicit native recovery; never signal a guessed process.
    ci_processes.verify_uid_idle(uid)


def pull_request_for_run(config, run):
    refs = run.get('pull_requests')
    if not isinstance(refs, list) or len(refs) != 1 or not isinstance(refs[0], dict) or not positive(refs[0].get('number')):
        raise ValueError('PR reference is missing or ambiguous')
    return api(config, 'repos/' + config['seat']['repository'] + '/pulls/' + str(refs[0]['number']))

def close_reservation(handles):
    for handle in handles:
        if isinstance(handle, int):
            os.close(handle)
        else:
            handle.close()

def registration_labels(config, state=None):
    standard = [config['seat']['label']]
    if state is not None:
        labels = state.get('runner_labels', standard)
        if labels != standard or not isinstance(labels, list):
            raise ValueError('Recorded runner labels are not approved')
        return labels
    return standard

def protected(path, directory=False):
    path = Path(path)
    for entry in (path, *path.parents):
        info = entry.lstat()
        if stat.S_ISLNK(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
            raise ValueError('Supervisor input is not root-owned and immutable')
    if directory and not path.is_dir():
        raise ValueError('Supervisor directory is missing')
    return path

def write_json(path, value):
    temporary = path.with_name(path.name + '.' + uuid.uuid4().hex + '.new')
    with temporary.open('x') as stream:
        json.dump(value, stream, sort_keys=True)
        stream.flush()
        os.fsync(stream.fileno())
    temporary.chmod(0o644)
    os.replace(temporary, path)

def api(config, endpoint, method='GET', allow_missing=False):
    if not endpoint.startswith('repos/' + config['seat']['repository'] + '/'):
        raise ValueError('Controller API outside the selected repository')
    # A system LaunchDaemon has no owner's GUI bootstrap/keychain session.
    # Use that existing session only for the controller API process; CI jobs
    # keep their separate UID, HOME and credential-free environment below.
    if not positive(config.get('controller_uid')):
        raise ValueError('Controller session UID is invalid')
    command = ['/bin/launchctl', 'asuser', str(config['controller_uid']),
               '/usr/bin/sudo', '-n', '-H', '-u', config['controller_user'], '--',
               str(controller_executable()), 'api', endpoint, '--method', method]
    result = subprocess.run(command, env={'PATH': '/usr/bin:/bin:/usr/sbin:/sbin'},
                            cwd=ROOT, capture_output=True, text=True, timeout=45)
    if result.returncode:
        if allow_missing and 'HTTP 404' in result.stderr:
            return None
        raise ValueError('Controller API failed; credentials and response are withheld')
    return json.loads(result.stdout) if result.stdout else None

def native_busy():
    commands = subprocess.check_output(['/bin/ps', '-A', '-o', 'comm='], text=True, timeout=10)
    arguments = subprocess.check_output(['/bin/ps', '-A', '-ww', '-o', 'args='], text=True, timeout=10)
    return (any(line.strip().endswith(('/MacOS/Unity', '/xcodebuild')) for line in commands.splitlines())
            or any('org.gradle.launcher.daemon.bootstrap.GradleDaemon' in line.split()
                   for line in arguments.splitlines()))

def peer_uid(connection):
    library = ctypes.CDLL('/usr/lib/libSystem.B.dylib', use_errno=True)
    library.getpeereid.argtypes = [ctypes.c_int, ctypes.POINTER(ctypes.c_uint32), ctypes.POINTER(ctypes.c_uint32)]
    library.getpeereid.restype = ctypes.c_int
    uid, gid = ctypes.c_uint32(), ctypes.c_uint32()
    if library.getpeereid(connection.fileno(), ctypes.byref(uid), ctypes.byref(gid)) != 0:
        raise ValueError('Cannot authenticate the local hook peer')
    return uid.value

def sandbox_profile(generation):
    # Sandbox inheritance prevents checkout/actions/children from persisting
    # outside the disposable tree, even in globally writable /tmp. Normal
    # read/execute/network access remains available for existing host checks.
    return ('(version 1) (allow default) (deny file-write*) '
            '(allow file-write* (subpath ' + json.dumps(str(generation)) + ') '
            '(literal "/dev/null") (literal "/dev/tty") (regex #"^/dev/fd/[0-9]+$"))')

def state_path(config):
    return ROOT / 'current' / (config['seat_id'] + '.json')

def runner_matches(config, state, runner):
    return (isinstance(runner, dict) and positive(runner.get('id'))
            and runner.get('name') == state['runner_name']
            and sorted(label.get('name', '') for label in runner.get('labels', []) if isinstance(label, dict))
            == registration_labels(config, state))

def credentials_cleanup(application, expected):
    # Anchor deletion in the original directory inode. A job may rename the
    # runner or replace it with a link, but root must never follow that link.
    try:
        fd = os.open(application, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    except OSError:
        return False
    try:
        info = os.fstat(fd)
        if [info.st_dev, info.st_ino] != expected:
            return False
        for name in ('.credentials', '.credentials_rsaparams'):
            try:
                os.unlink(name, dir_fd=fd)
            except FileNotFoundError:
                pass
        return True
    finally:
        os.close(fd)

def reconcile(config, uid):
    """Recover only this root-recorded nonce/UID, after GitHub proves idle.

    An interrupted registration never uses --replace. Its unique recorded
    name allows exact recovery even if config.sh died before saving .runner.
    Busy jobs and foreign runner identities are never stopped or deleted.
    """
    ci_processes.verify_uid_idle(uid)
    path = state_path(config)
    if not path.exists():
        return
    state = json.loads(protected(path).read_text())
    seat = config['seat']
    if (state.get('seat_id') != config['seat_id'] or state.get('uid') != uid
            or not re.fullmatch(re.escape(config['seat_id']) + '-[0-9a-f]{32}', state.get('generation', ''))
            or state.get('runner_name') != seat['name'] + '-' + state['generation'].split('-', 1)[1]):
        raise ValueError('Recovery identity is not owned by this seat')
    prefix = 'repos/' + seat['repository']
    runners, expected = [], None
    for page in range(1, 4):
        collection = api(config, prefix + '/actions/runners?per_page=100&page=' + str(page))
        count, batch = collection.get('total_count'), collection.get('runners')
        if (type(count) is not int or not 0 <= count <= 300 or not isinstance(batch, list)
                or expected not in (None, count)):
            raise ValueError('Runner inventory changed or exceeded the recovery bound')
        expected = count
        runners.extend(batch)
        if len(runners) == count:
            break
        if len(batch) != 100:
            raise ValueError('Incomplete runner recovery inventory')
    if len(runners) != expected or any(not isinstance(r, dict) or not positive(r.get('id')) for r in runners):
        raise ValueError('Incomplete runner recovery inventory')
    matches = [r for r in runners if r.get('name') == state['runner_name']]
    if len(matches) > 1:
        raise ValueError('Ambiguous recovery registration')
    runner = matches[0] if matches else None
    admitted = state.get('admission')
    if admitted is not None:
        if not isinstance(admitted, dict) or not positive(admitted.get('job_id')):
            raise ValueError('Recovery job identity is incomplete')
        job = api(config, prefix + '/actions/jobs/' + str(admitted['job_id']))
        if (not isinstance(job, dict) or job.get('id') != admitted['job_id']
                or job.get('runner_id') != state.get('runner_id')
                or job.get('head_sha') != admitted.get('head_sha') or job.get('status') != 'completed'):
            raise ValueError('Previous job completion is not proven; preserve processes and reservation')
    if runner is not None:
        if not runner_matches(config, state, runner) or (state.get('runner_id') not in (None, runner['id'])):
            raise ValueError('Recovery registration changed; never remove another runner')
        if runner.get('busy') is not False:
            raise ValueError('Previous owned job is still active; preserve its reservation')
        api(config, prefix + '/actions/runners/' + str(runner['id']), 'DELETE')
        if api(config, prefix + '/actions/runners/' + str(runner['id']), allow_missing=True) is not None:
            raise ValueError('Owned registration removal was not confirmed')
    stop_owned_processes(uid)
    cleanup_dotnet_group(state)
    handles = []
    try:
        for name in ('host_lock', 'ios_lock'):
            fd = os.open(config[name], os.O_RDWR | os.O_NOFOLLOW)
            handles.append(fd)
            info = os.fstat(fd)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != config['controller_uid'] or info.st_mode & 0o077:
                raise ValueError('Recovery owner lock changed')
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        marker = Path(config['host_lock'] + '.pending') / ('general-' + config['seat_id'] + '-' +
                  str(state.get('runner_id') or (runner['id'] if runner else 0)))
        marker_identity = None
        if marker.exists():
            info = marker.lstat()
            if not stat.S_ISREG(info.st_mode) or info.st_uid != config['controller_uid'] or info.st_mode & 0o077:
                raise ValueError('Recovery reservation is not owned')
            body = json.loads(marker.read_text())
            if body.get('uid') != uid or body.get('runner_id') != state.get('runner_id'):
                raise ValueError('Recovery reservation identity changed')
            marker_identity = (info.st_dev, info.st_ino)
        endpoint = RUNTIME / 'sockets' / (config['seat_id'] + '.sock')
        if endpoint.exists():
            info = endpoint.lstat()
            if not stat.S_ISSOCK(info.st_mode) or info.st_uid != uid:
                raise ValueError('Recovery socket is not owned')
            endpoint.unlink()
        generation = RUNTIME / 'jobs' / state['generation']
        if generation.exists():
            info = generation.lstat()
            if not stat.S_ISDIR(info.st_mode) or info.st_uid not in (0, uid):
                raise ValueError('Recovery tree is not owned')
            generation.chmod(0o700)
            os.chown(generation, 0, 0)
            credentials_cleanup(generation / 'runner', state['application_inode'])
            retained = ROOT / 'evidence' / (generation.name + '-recovery-' + uuid.uuid4().hex)
            generation.rename(retained)
            write_json(retained / 'recovery.json', {'registration': state, 'uid_stopped': True})
        if marker_identity is not None:
            info = marker.lstat()
            if (info.st_dev, info.st_ino) != marker_identity:
                raise ValueError('Recovery reservation changed before release')
            marker.unlink()
        path.unlink()
    finally:
        close_reservation(handles)

class Admission:
    def __init__(self, config, uid, runner_id, socket_path):
        self.config, self.uid, self.runner_id = config, uid, runner_id
        self.path = socket_path
        self.stop = threading.Event()
        self.lease = []
        self.result = None
        self.marker = None
        self.server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.server.bind(str(socket_path))
        os.chown(socket_path, uid, -1)
        socket_path.chmod(0o600)
        self.server.listen(2)
        self.server.settimeout(1)

    def accept_job(self, environment):
        config, seat = self.config, self.config['seat']
        if not environment.get('GITHUB_RUN_ID', '').isdigit() or not environment.get('GITHUB_RUN_ATTEMPT', '').isdigit():
            raise ValueError('Invalid run identity')
        prefix = 'repos/' + seat['repository']
        run = api(config, prefix + '/actions/runs/' + environment['GITHUB_RUN_ID'])
        pr = None
        if run.get('event') == 'pull_request':
            pr = pull_request_for_run(config, run)
        approvals_path = protected(ROOT / 'approvals.json')
        identity = authorize(seat, run, environment, pr, json.loads(approvals_path.read_text()))
        jobs = api(config, prefix + '/actions/runs/%d/attempts/%d/jobs?per_page=100' %
                   (identity['run_id'], identity['run_attempt']))
        active = [job for job in jobs.get('jobs', []) if job.get('runner_id') == self.runner_id
                  and job.get('status') == 'in_progress' and job.get('head_sha') == identity['head_sha']]
        if jobs.get('total_count', 101) > 100 or len(active) != 1:
            raise ValueError('This runner is not assigned exactly one authentic job')
        identity['job_id'] = active[0]['id']
        if self.result is not None:
            raise ValueError('This disposable seat already admitted a job')
        deadline = time.monotonic() + ADMISSION_WAIT_SECONDS
        while not self.stop.is_set() and time.monotonic() < deadline:
            handles = []
            try:
                handles = host_reservation(config)
                pending = Path(config['host_lock'] + '.pending')
                # Re-read the run after waiting. A cancellation/rerun/head update
                # may never acquire a lease from an earlier API snapshot.
                current = api(config, prefix + '/actions/runs/' + str(identity['run_id']))
                if (current.get('run_attempt') != identity['run_attempt']
                        or current.get('head_sha') != identity['head_sha']
                        or current.get('status') != 'in_progress'):
                    raise ValueError('Run changed while waiting for the host')
                if pr is not None:
                    latest_pr = api(config, prefix + '/pulls/' + str(pr['number']))
                    authorize(seat, current, environment, latest_pr, json.loads(approvals_path.read_text()))
                if self.stop.is_set():
                    raise ValueError('Runner stopped before admission')
                marker = pending / ('general-' + config['seat_id'] + '-' + str(self.runner_id))
                current_state = json.loads(protected(state_path(config)).read_text())
                if current_state.get('runner_id') != self.runner_id or current_state.get('uid') != self.uid:
                    raise ValueError('Durable admission registration changed')
                current_state['admission'] = identity
                write_json(state_path(config), current_state)
                # A crash/reboot drops flock but must not remove the reservation.
                # Recovery requires stopping this dedicated UID first.
                with marker.open('x') as stream:
                    json.dump(dict(identity, uid=self.uid, runner_id=self.runner_id), stream)
                    stream.flush()
                    os.fsync(stream.fileno())
                marker.chmod(0o600)
                os.chown(marker, config['controller_uid'], -1)
                self.marker = marker
                self.lease, self.result = handles, identity
                return identity
            except BlockingIOError:
                for fd in handles:
                    os.close(fd)
                self.stop.wait(2)
            except BaseException:
                for fd in handles:
                    os.close(fd)
                raise
        raise ValueError('Host reservation timed out or runner stopped')

    def serve(self):
        while not self.stop.is_set():
            try:
                connection, _ = self.server.accept()
            except socket.timeout:
                continue
            with connection:
                connection.settimeout(10)
                response = {'ok': False}
                try:
                    if peer_uid(connection) != self.uid:
                        raise ValueError('Wrong CI account')
                    data = bytearray()
                    while not data.endswith(b'\n') and len(data) <= 8192:
                        chunk = connection.recv(4096)
                        if not chunk:
                            break
                        data.extend(chunk)
                    if len(data) > 8192:
                        raise ValueError('Oversized hook request')
                    identity = self.accept_job(json.loads(data))
                    response = {'ok': True, 'identity': identity}
                except Exception:
                    pass
                try:
                    connection.sendall(json.dumps(response).encode() + b'\n')
                except OSError:
                    pass

    def close(self):
        self.stop.set()
        if processes(self.uid):
            raise ValueError('CI UID remains live; reservation must survive')
        if self.marker is not None:
            self.marker.unlink()
        close_reservation(self.lease)
        self.server.close()
        self.path.unlink(missing_ok=True)

def host_reservation(config, growth_bytes=0):
    """Reuse the owner locks and native admission limits before allocation too."""
    handles = []
    try:
        for name in ('host_lock', 'ios_lock'):
            path = Path(config[name])
            info = path.lstat()
            if not stat.S_ISREG(info.st_mode) or info.st_uid != config['controller_uid'] or info.st_mode & 0o077:
                raise ValueError('Owner host lock changed; do not weaken permissions')
            fd = os.open(path, os.O_RDWR | os.O_NOFOLLOW)
            handles.append(fd)
            opened = os.fstat(fd)
            if (opened.st_dev, opened.st_ino) != (info.st_dev, info.st_ino):
                raise ValueError('Host lock inode changed')
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        metrics = collect_metrics()
        disk = os.statvfs(RUNTIME)
        disk_busy = disk.f_bavail * disk.f_frsize < DISK_FLOOR + growth_bytes
        pending = Path(config['host_lock'] + '.pending')
        pending_info = pending.lstat()
        if (not stat.S_ISDIR(pending_info.st_mode) or pending_info.st_uid != config['controller_uid']
                or pending_info.st_mode & 0o077):
            raise ValueError('Owner reservation directory changed')
        if (native_busy() or metrics['cpu_percent'] > 65 or metrics['memory_available_mb'] < 6144
                or disk_busy or any(pending.iterdir())):
            raise BlockingIOError('Host resources/reservations are busy')
        return handles
    except BaseException:
        for fd in handles:
            os.close(fd)
        raise

def runner_expansion_bytes(archive_path):
    # Include allocated blocks, directory entries and the small registration
    # state. The root-owned, reviewed archive is also the one extracted below.
    with tarfile.open(archive_path) as archive:
        return ALLOCATION_MARGIN + sum(((member.size + 4095) // 4096 + 1) * 4096
                                       for member in archive)

def allocation_reservation(config, archive_path):
    growth = runner_expansion_bytes(archive_path)
    while True:
        try:
            return host_reservation(config, growth)
        except BlockingIOError:
            time.sleep(2)

def dotnet_group_identity(state):
    generation = state.get('generation', '')
    if not re.fullmatch(re.escape(state['seat_id']) + '-[0-9a-f]{32}', generation):
        raise ValueError('Invalid .NET generation identity')
    # .NET's Apple application-group buffer permits at most 13 characters.
    # Keep the complete nonce in the root state; a short per-seat alias remains
    # collision checked and must point to that exact full generation.
    if state.get('seat_id') != 'exporter' or state.get('uid') != 402:
        raise ValueError('Exporter identity required')
    prefix = 'me'
    name = prefix + generation.split('-', 1)[1][:11]
    if state.get('dotnet_application_group_id') not in (None, name):
        raise ValueError('.NET group identity changed')
    return name, RUNTIME / 'jobs' / generation / 'tmp' / 'dotnet-ipc'

def prepare_dotnet_group(state):
    name, target = dotnet_group_identity(state)
    # Validate existing parents without chmod or following a symlink. This
    # home is shared by the two disabled CI identities, but only root creates
    # the unique per-generation alias. CI can write only its resolved target.
    for directory in reversed((DOTNET_GROUPS, DOTNET_GROUPS.parent)):
        if not directory.exists() and not directory.is_symlink():
            protected(directory.parent, directory=True)
            directory.mkdir(mode=0o755)
            # A root maintenance process can have umask 077. Set the mode of
            # only this newly created directory so either CI UID can traverse.
            directory.chmod(0o755)
        protected(directory, directory=True)
    link = DOTNET_GROUPS / name
    if link.exists() or link.is_symlink():
        raise ValueError('An application group already exists; preserve it')
    link.symlink_to(target, target_is_directory=True)
    return name

def cleanup_dotnet_group(state):
    name, target = dotnet_group_identity(state)
    if state.get('dotnet_application_group_id') is None:
        return  # Old registrations never created an application group.
    if processes(state['uid']):
        raise ValueError('CI UID remains live; preserve its application group')
    parent = DOTNET_GROUPS
    while not parent.exists() and not parent.is_symlink():
        parent = parent.parent
    protected(parent, directory=True)
    if parent != DOTNET_GROUPS:
        return  # A crash can precede first creation of either group parent.
    link = DOTNET_GROUPS / name
    try:
        info = link.lstat()
    except FileNotFoundError:
        return  # Recorded intent can precede creation or survive cleanup.
    if not stat.S_ISLNK(info.st_mode) or info.st_uid != 0 or os.readlink(link) != str(target):
        raise ValueError('Recorded application group changed; preserve it')
    link.unlink()

def prepare_runner(config, account):
    seat = config['seat']
    archive_path = protected(ROOT / 'runner.tar.gz')
    handles = allocation_reservation(config, archive_path)
    try:
        generation = RUNTIME / 'jobs' / (config['seat_id'] + '-' + uuid.uuid4().hex)
        generation.mkdir(mode=0o700)
        application = generation / 'runner'
        application.mkdir()
        inode = application.stat()
        runner_name = seat['name'] + '-' + generation.name.split('-', 1)[1]
        current = {'seat_id': config['seat_id'], 'uid': account.pw_uid, 'generation': generation.name,
                   'runner_name': runner_name, 'runner_id': None,
                   'application_inode': [inode.st_dev, inode.st_ino]}
        current['runner_labels'] = registration_labels(config)
        write_json(state_path(config), current)
        with tarfile.open(archive_path) as archive:
            archive.extractall(application, filter='data')
        home = generation / 'home'
        home.mkdir()
        temporary = generation / 'tmp'
        temporary.mkdir()
        (temporary / 'dotnet-ipc').mkdir()
        current['dotnet_application_group_id'] = dotnet_group_identity(current)[0]
        write_json(state_path(config), current)
        prepare_dotnet_group(current)
        for path in (generation, *generation.rglob('*')):
            if path.is_symlink():
                # Official Node/npm links are internal to this verified archive.
                if not path.resolve().is_relative_to(application):
                    raise ValueError('Runner archive link escapes its application')
            os.chown(path, account.pw_uid, account.pw_gid, follow_symlinks=False)
        environment = {
            'HOME': str(home), 'TMPDIR': str(temporary) + '/', 'LANG': 'en_US.UTF-8',
            'PATH': str(generation / 'npm/bin') + ':' + str(ROOT / 'bin') + ':/usr/bin:/bin:/usr/sbin:/sbin',
            'NPM_CONFIG_PREFIX': str(generation / 'npm'),
            'DOTNET_SANDBOX_APPLICATION_GROUP_ID': current['dotnet_application_group_id'],
            'DOTNET_ROOT': str(RUNTIME / 'tools/dotnet'),
            'DOTNET_INSTALL_DIR': str(generation / 'dotnet'),
            'JAVA_HOME': str(RUNTIME / 'tools/jdk'),
            # macOS Java ignores TMPDIR for its default java.io.tmpdir.
            # Set the JVM property for every child while keeping the same sandbox.
            'JAVA_TOOL_OPTIONS': '-Djava.io.tmpdir=' + str(temporary),
            'JAVA_HOME_17_X64': str(RUNTIME / 'tools/jdk'),
            'ANDROID_HOME': str(RUNTIME / 'tools/android-sdk'),
            'ANDROID_SDK_ROOT': str(RUNTIME / 'tools/android-sdk'),
            'PYTHONUSERBASE': str(generation / 'python-user'),
            'RUNNER_TOOL_CACHE': str(generation / 'tool-cache'),
            'ACTIONS_RUNNER_HOOK_JOB_STARTED': str(RUNTIME / 'hooks' / (config['seat_id'] + '.sh')),
            'EXPORTER_CI_ADMISSION_SOCKET': str(RUNTIME / 'sockets' / (config['seat_id'] + '.sock')),
            'EXPORTER_CI_PYTHON': str(RUNTIME / 'tools/python/bin/python3.12'),
        }
        def demote():
            os.setgroups([account.pw_gid])
            os.setgid(account.pw_gid)
            os.setuid(account.pw_uid)
        prefix = 'repos/' + seat['repository']
        token = api(config, prefix + '/actions/runners/registration-token', 'POST')['token']
        environment['ACTIONS_RUNNER_INPUT_TOKEN'] = token
        configured = subprocess.run([str(application / 'config.sh'), '--unattended', '--ephemeral',
            '--disableupdate', '--no-default-labels', '--url', 'https://github.com/' + seat['repository'],
            '--name', runner_name, '--labels', ','.join(current['runner_labels']), '--work', '_work'],
            cwd=application, env=environment, preexec_fn=demote, capture_output=True, timeout=120)
        environment.pop('ACTIONS_RUNNER_INPUT_TOKEN', None)
        token = None
        if configured.returncode:
            raise ValueError('Disposable runner registration failed; keep root-protected recovery data')
        marker = json.loads((application / '.runner').read_text(encoding='utf-8-sig'))
        runner_id = marker.get('agentId')
        if not positive(runner_id):
            raise ValueError('Runner did not report a positive registration ID')
        current['runner_id'] = runner_id
        write_json(state_path(config), current)
        registered = api(config, prefix + '/actions/runners/' + str(runner_id))
        if (registered.get('id') != runner_id or registered.get('name') != runner_name
                or sorted(label['name'] for label in registered.get('labels', [])) != current['runner_labels']):
            raise ValueError('Runner registration readback mismatched')
        if not positive(marker.get('poolId')) or not isinstance(marker.get('poolName'), str) or not marker['poolName']:
            raise ValueError('Runner group provenance is incomplete')
        record = {'id': runner_id, 'name': runner_name, 'labels': current['runner_labels'],
                  'group_id': marker['poolId'], 'group_name': marker['poolName']}
        registry_path = ROOT / 'registries' / 'exporter.json'
        registry = json.loads(protected(registry_path).read_text())
        registry['registrations'].append(record)
        registry['registrations'] = registry['registrations'][-10000:]
        write_json(registry_path, registry)
        return generation, application, current, environment, record
    finally:
        # Never hold host locks while an idle listener waits for GitHub jobs.
        close_reservation(handles)

def run_cycle(config):
    seat = config['seat']
    account = pwd.getpwnam(seat['user'])
    if (account.pw_uid != 402 or account.pw_gid != 402 or account.pw_uid == config['controller_uid']
            or account.pw_shell != '/usr/bin/false' or account.pw_dir != str(RUNTIME / 'home')
            or seat['user'] in grp.getgrnam('admin').gr_mem
            or account.pw_gid == grp.getgrnam('admin').gr_gid):
        raise ValueError('Unsafe CI account UID')
    protected(RUNTIME / 'home', directory=True)
    reconcile(config, account.pw_uid)
    if processes(account.pw_uid):
        raise ValueError('Previous CI processes exist; do not overwrite recovery data')
    generation, application, current, environment, record = prepare_runner(config, account)
    runner_id = current['runner_id']
    prefix = 'repos/' + seat['repository']
    def demote():
        os.setgroups([account.pw_gid])
        os.setgid(account.pw_gid)
        os.setuid(account.pw_uid)
    admission = Admission(config, account.pw_uid, runner_id, Path(environment['EXPORTER_CI_ADMISSION_SOCKET']))
    thread = threading.Thread(target=admission.serve, daemon=True)
    process = subprocess.Popen(['/usr/bin/sandbox-exec', '-p', sandbox_profile(generation),
                                str(application / 'run.sh')], cwd=application,
                               env=environment, preexec_fn=demote, start_new_session=True,
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    captured = ci_processes.capture_process(process.pid, account.pw_uid)
    # Demote before the admission thread exists: avoid preexec_fn after a
    # multi-threaded fork. The already-bound socket queues an early hook.
    thread.start()
    try:
        process.wait()
    finally:
        admission.stop.set()
        # Never release a lease while an admission request can still create it.
        thread.join()
        ci_processes.stop_and_verify(captured, account.pw_uid)
        cleanup_dotnet_group(current)
        # Host locks are held until all processes belonging to this job stopped.
        admission.close()
        generation.chmod(0o700)
        os.chown(generation, 0, 0)
        credentials_removed = credentials_cleanup(application, current['application_inode'])
        job = None
        if admission.result:
            job = api(config, prefix + '/actions/jobs/' + str(admission.result['job_id']))
        receipt = {'runner': record, 'admission': admission.result, 'runner_exit_code': process.returncode,
                   'credentials_removed': credentials_removed,
                   'job_status': job.get('status') if isinstance(job, dict) else None,
                   'job_conclusion': job.get('conclusion') if isinstance(job, dict) else None}
        retained = ROOT / 'evidence' / generation.name
        retained.mkdir(mode=0o700)
        write_json(retained / 'receipt.json', receipt)
        if (isinstance(job, dict) and job.get('id') == admission.result['job_id']
                and job.get('runner_id') == runner_id and job.get('head_sha') == admission.result['head_sha']
                and job.get('status') == 'completed' and job.get('conclusion') == 'success'
                and process.returncode == 0):
            # Required artifacts have already been published by the job. Keep
            # its receipt; remove only this stopped, successful disposable tree.
            if not shutil.rmtree.avoids_symlink_attacks:
                raise ValueError('Descriptor-safe cleanup is unavailable')
            shutil.rmtree(generation)
        else:
            # Failed/unconfirmed work is root-private and kept for diagnosis.
            generation.rename(retained / 'work')

def load_seat_config(seat_id):
    config_path = protected(ROOT / (seat_id + '.json'))
    config = json.loads(config_path.read_text())
    # The installer writes JSON, whose arrays round-trip as lists. Compare
    # the exact persisted representation without relaxing policy contents.
    expected = json.loads(json.dumps(SEATS[seat_id]))
    if (config.get('seat') != expected or config.get('seat_id') != seat_id
            or config.get('controller_gh') != str(CONTROLLER_GH)):
        raise ValueError('Installed seat policy changed')
    return config


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--seat', choices=('exporter',), required=True)
    args = parser.parse_args()
    if os.getuid() != 0 or os.uname().sysname != 'Darwin':
        raise ValueError('A reviewed root launch daemon on a physical Mac is required')
    for directory in ('jobs', 'sockets', 'hooks'):
        protected(RUNTIME / directory, directory=True)
    controller_executable()
    config = load_seat_config(args.seat)
    with protected(ROOT / 'exporter.service.lock').open('r') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        while True:
            run_cycle(config)


if __name__ == '__main__':
    main()
