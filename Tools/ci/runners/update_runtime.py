"""Switch the stopped existing Exporter service to reviewed repository-owned code.

This updater never starts/stops services, registers/removes runners, cancels jobs
or opens runner credentials. A pinned plan and root-private non-replay journal
preserve partial updates for manual review. Run only from reviewed root staging.
"""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import plistlib
import pwd
import re
import shlex
import stat
import subprocess

import ci_processes
import policy
import supervisor

ROOT = supervisor.ROOT
RUNTIME = supervisor.RUNTIME
PYTHON = RUNTIME / 'tools/python/bin/python3.12'
LABEL = 'jp.mitsuya0077.mysi.general-exporter'
PLIST = Path('/Library/LaunchDaemons') / (LABEL + '.plist')
CONFIG = ROOT / 'exporter.json'
HOOK = RUNTIME / 'hooks/exporter.sh'
LOCK = ROOT / 'exporter.service.lock'
CURRENT = ROOT / 'current/exporter.json'
REGISTRY = ROOT / 'registries/exporter.json'
OWNER = 501
UID = 402
FILES = frozenset(('supervisor.py', 'policy.py', 'resources.py', 'hook.py', 'ci_processes.py'))
ENV = {'PATH': '/usr/bin:/bin:/usr/sbin:/sbin', 'LANG': 'C'}


def need(value, reason):
    if not value:
        raise ValueError(reason)


def canonical(value):
    return (json.dumps(value, sort_keys=True, separators=(',', ':')) + '\n').encode()


def sha(value):
    return hashlib.sha256(value).hexdigest()


def read(path, owner=0, staging=False):
    path = Path(path)
    if not staging:
        supervisor.protected(path)
    before = path.lstat()
    need(stat.S_ISREG(before.st_mode) and before.st_uid == owner and before.st_nlink == 1
         and not before.st_mode & 0o022 and before.st_size <= 1024 * 1024, 'REGULAR_FILE_REQUIRED')
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    try:
        opened = os.fstat(fd)
        need((opened.st_dev, opened.st_ino) == (before.st_dev, before.st_ino), 'FILE_CHANGED')
        with os.fdopen(fd, 'rb', closefd=False) as stream:
            raw = stream.read(1024 * 1024 + 1)
        after = os.fstat(fd)
        fields = ('st_dev', 'st_ino', 'st_mode', 'st_nlink', 'st_uid', 'st_gid', 'st_size', 'st_mtime_ns', 'st_ctime_ns')
        need(len(raw) == before.st_size and all(getattr(after, key) == getattr(opened, key) for key in fields), 'FILE_CHANGED')
        return raw
    finally:
        os.close(fd)


def unloaded():
    result = subprocess.run(['/bin/launchctl', 'print', 'system/' + LABEL],
                            env=ENV, capture_output=True, text=True, timeout=30)
    need(result.returncode != 0 and 'Could not find service' in result.stdout + result.stderr
         and LABEL in result.stdout + result.stderr, 'EXPORTER_SERVICE_MUST_BE_UNLOADED')


def load_stage(stage, pin):
    info = stage.lstat()
    need(stat.S_ISDIR(info.st_mode) and info.st_uid == OWNER and stat.S_IMODE(info.st_mode) == 0o700,
         'PRIVATE_STAGE_REQUIRED')
    raw = read(stage / 'manifest.json', OWNER, staging=True)
    need(sha(raw) == pin, 'MANIFEST_PIN')
    manifest = json.loads(raw)
    need(set(manifest) == {'schema', 'source_sha', 'files'} and manifest['schema'] == 1
         and type(manifest['schema']) is int and re.fullmatch('[0-9a-f]{40}', manifest['source_sha'])
         and set(manifest['files']) == FILES
         and {p.name for p in stage.iterdir()} == FILES | {'manifest.json'}, 'MANIFEST_SCHEMA')
    payload = {name: read(stage / name, OWNER, staging=True) for name in FILES}
    for name, value in payload.items():
        need(sha(value) == manifest['files'][name], 'PAYLOAD_PIN')
        compile(value, name, 'exec')
    return manifest, payload


def existing_seat(configuration):
    expected = json.loads(json.dumps(policy.SEATS['exporter']))
    seat = configuration.get('seat')
    need(isinstance(seat, dict), 'EXPORTER_SEAT_REQUIRED')
    canonical_seat = dict(seat, repository=policy.REPOSITORY)
    need(canonical_seat == expected and seat.get('repository') in policy.LEGACY_REPOSITORIES | {policy.REPOSITORY}
         and configuration.get('seat_id') == 'exporter' and configuration.get('controller_uid') == OWNER
         and configuration.get('controller_user') == 'mouri', 'EXISTING_EXPORTER_IDENTITY')
    # Only this already installed role is allowed. Account changes are outside
    # migration scope and must never fall back to another OS identity.
    account = pwd.getpwnam(expected['user'])
    need(account.pw_uid == account.pw_gid == UID and account.pw_shell == '/usr/bin/false'
         and account.pw_dir == str(RUNTIME / 'home'), 'EXISTING_EXPORTER_ACCOUNT')
    return expected


def snapshot():
    unloaded()
    ci_processes.verify_uid_idle(UID)
    controller = supervisor.controller_executable().lstat()
    python = supervisor.protected(PYTHON).lstat()
    configuration = json.loads(read(CONFIG))
    existing_seat(configuration)
    definition = plistlib.loads(read(PLIST))
    arguments = definition.get('ProgramArguments')
    need(definition.get('Label') == LABEL and definition.get('WorkingDirectory') == str(ROOT)
         and definition.get('UserName') in (None, 'root') and isinstance(arguments, list)
         and arguments and arguments[0] == str(PYTHON)
         and arguments[-2:] == ['--seat', 'exporter'], 'EXISTING_EXPORTER_SERVICE')
    supervisor.protected(PYTHON)
    preserved = {str(REGISTRY): sha(read(REGISTRY)), str(ROOT / 'approvals.json'): sha(read(ROOT / 'approvals.json'))}
    if os.path.lexists(CURRENT):
        raw = read(CURRENT); current = json.loads(raw)
        need(current.get('seat_id') == 'exporter' and current.get('uid') == UID
             and re.fullmatch('exporter-[0-9a-f]{32}', current.get('generation', ''))
             and current.get('admission') is None, 'CURRENT_REQUIRES_NATIVE_RECOVERY')
        preserved[str(CURRENT)] = sha(raw)
    else:
        preserved[str(CURRENT)] = None
    for name in ('host_lock', 'ios_lock'):
        path = Path(configuration[name]); info = path.lstat()
        need(stat.S_ISREG(info.st_mode) and info.st_uid == OWNER and stat.S_IMODE(info.st_mode) == 0o600,
             'HOST_LOCK_IDENTITY')
        preserved[str(path)] = [info.st_dev, info.st_ino, info.st_uid, info.st_mode]
    pending = Path(configuration['host_lock'] + '.pending')
    info = pending.lstat()
    need(stat.S_ISDIR(info.st_mode) and not stat.S_ISLNK(info.st_mode)
         and info.st_uid == OWNER and stat.S_IMODE(info.st_mode) == 0o700, 'RESERVATION_DIRECTORY')
    need(not any(p.name.startswith('general-exporter-') for p in pending.iterdir()), 'EXPORTER_ACTIVE_RESERVATION')
    return {'configuration': configuration, 'definition': definition,
            'baseline_sha256': {str(p): sha(read(p)) for p in (CONFIG, PLIST, HOOK)},
            'preserved': preserved,
            'executables': {str(path): [getattr(info, field) for field in
                ('st_dev', 'st_ino', 'st_mode', 'st_uid', 'st_gid', 'st_size', 'st_mtime_ns', 'st_ctime_ns')]
                for path, info in ((supervisor.CONTROLLER_GH, controller), (PYTHON, python))}}


def create(path, value, mode):
    supervisor.protected(path.parent, directory=True)
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, mode)
    try:
        os.fchmod(fd, mode)
        os.fchown(fd, 0, 0)
        with os.fdopen(fd, 'wb', closefd=False) as stream:
            stream.write(value); stream.flush(); os.fsync(fd)
    finally:
        os.close(fd)
    need(read(path) == value, 'CREATED_BYTE_READBACK')
    sync_directory(path.parent)


def sync_directory(path):
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def mkdir(path):
    supervisor.protected(path.parent, directory=True)
    path.mkdir(mode=0o755)
    path.chmod(0o755)
    os.chown(path, 0, 0)
    sync_directory(path.parent)


def replace(path, value):
    before = path.lstat()
    temporary = path.with_name(path.name + '.exporter-update-new')
    create(temporary, value, stat.S_IMODE(before.st_mode))
    os.replace(temporary, path)
    sync_directory(path.parent)
    need(read(path) == value, 'REPLACED_BYTE_READBACK')


def run(stage, manifest_pin, plan_pin=None):
    need(os.getuid() == os.geteuid() == 0 and os.uname().sysname == 'Darwin', 'NATIVE_ROOT_REQUIRED')
    manifest, payload = load_stage(stage, manifest_pin)
    lock = supervisor.protected(LOCK)
    fd = os.open(lock, os.O_RDONLY | os.O_NOFOLLOW)
    try:
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        before = snapshot()
        plan = {'schema': 1, 'source_sha': manifest['source_sha'], 'files': manifest['files'], 'before': before}
        pin = sha(canonical(plan))
        if plan_pin is None:
            return {'status': 'ready', 'plan_sha256': pin, 'source_sha': manifest['source_sha']}
        need(plan_pin == pin, 'PLAN_PIN')
        base = ROOT / 'exporter'
        code = base / 'code' / manifest['source_sha']
        journal = ROOT / ('exporter-runtime-update-' + manifest['source_sha'] + '.json')
        need(not os.path.lexists(journal) and not os.path.lexists(code), 'NO_AUTOMATIC_REPLAY')
        configuration = dict(before['configuration'], seat=existing_seat(before['configuration']),
                             controller_gh=str(supervisor.CONTROLLER_GH))
        definition = dict(before['definition'])
        bootstrap = 'import sys;sys.path.insert(0,' + repr(str(code)) + ');import supervisor;supervisor.main()'
        definition['ProgramArguments'] = [str(PYTHON), '-I', '-S', '-B', '-c', bootstrap, '--seat', 'exporter']
        hook = ('#!/bin/sh\nexec ' + shlex.quote(str(PYTHON)) + ' -I -S -B '
                + shlex.quote(str(code / 'hook.py')) + '\n').encode()
        need(snapshot() == before, 'PREAPPLY_STATE_CHANGED')
        create(journal, canonical(dict(plan, status='started', replay_allowed=False)), 0o600)
        for directory in (base, base / 'code'):
            if os.path.lexists(directory):
                supervisor.protected(directory, directory=True)
            else:
                mkdir(directory)
        mkdir(code)
        for name, value in payload.items():
            create(code / name, value, 0o644)
        need(snapshot() == before, 'STATE_CHANGED_BEFORE_SWITCH')
        replace(CONFIG, canonical(configuration))
        replace(HOOK, hook)
        replace(PLIST, plistlib.dumps(definition))
        after = snapshot()
        need(after['preserved'] == before['preserved'], 'PRESERVED_STATE_CHANGED')
        result = {'status': 'installed_stopped', 'source_sha': manifest['source_sha'],
                  'registration_mutated': False, 'credentials_read': False,
                  'services_started': False, 'online_proven': False, 'journal': str(journal)}
        create(ROOT / ('exporter-runtime-update-' + manifest['source_sha'] + '-completed.json'), canonical(result), 0o600)
        return result
    finally:
        os.close(fd)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--manifest-sha256', required=True)
    parser.add_argument('--apply-plan-sha256')
    args = parser.parse_args()
    try:
        print(json.dumps(run(args.stage, args.manifest_sha256, args.apply_plan_sha256), sort_keys=True))
        return 0
    except Exception as error:
        print(json.dumps({'status': 'held', 'error_type': type(error).__name__, 'replay_allowed': False}))
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
