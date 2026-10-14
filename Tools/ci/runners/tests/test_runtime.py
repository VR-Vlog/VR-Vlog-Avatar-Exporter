import importlib.util
import json
from pathlib import Path
import signal
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

SOURCE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SOURCE))
import ci_processes as ci
import policy
import supervisor
import update_runtime as updater


def row(pid=100, auxiliary=False, pgid=None):
    return {'pid': pid, 'uid': 402, 'pgid': pid if pgid is None else pgid,
            'start_sec': 123, 'start_usec': 456, 'auxiliary': auxiliary}


class Backend:
    def __init__(self, rows=()):
        self.rows = list(rows)
        self.signals = []

    def info(self, pid):
        for value in self.rows:
            if value['pid'] == pid:
                return {key: value[key] for key in ci.IDENTITY_KEYS}
        raise ci.Gone()

    def classify(self, pid, uid):
        return next(value for value in self.rows if value['pid'] == pid and value['uid'] == uid)

    def snapshot(self, uid):
        return list(self.rows)

    def terminate(self, value, sig):
        self.signals.append((value['pid'], sig))
        self.rows.remove(value)


class ProcessTests(unittest.TestCase):
    @mock.patch.object(ci.os, 'geteuid', return_value=0)
    def test_only_exporter_uid_and_verified_helpers_are_idle(self, _):
        backend = Backend([row(9, auxiliary=True)])
        self.assertEqual(ci.verify_uid_idle(402, backend=backend), {'ci_stopped': True, 'auxiliary_count': 1})
        for uid in (True, 0, 401, 403, 404, 405, 501):
            with self.assertRaises(ci.UnknownProcesses):
                ci.verify_uid_idle(uid, backend=backend)
        self.assertEqual(backend.signals, [])
        with self.assertRaises(ci.UnknownProcesses):
            ci.verify_uid_idle(402, backend=Backend([row()]))

    @mock.patch.object(ci.os, 'geteuid', return_value=0)
    def test_reaped_once_leader_stops_daemonized_work_without_signalling_helpers(self, _):
        captured = {key: value for key, value in row().items() if key != 'auxiliary'}
        backend = Backend([row(201, pgid=77), row(9, auxiliary=True)])
        result = ci.stop_and_verify(captured, 402, backend=backend, sleep=lambda _: None)
        self.assertEqual(result, {'ci_stopped': True, 'auxiliary_count': 1})
        self.assertEqual(backend.signals, [(201, signal.SIGTERM)])
        live = Backend([row(), row(201)])
        with self.assertRaises(ci.UnknownProcesses):
            ci.stop_and_verify(captured, 402, backend=live)
        self.assertEqual(live.signals, [])


class PolicyTests(unittest.TestCase):
    def fixture(self, event='push', actor='mitsuya0077'):
        sha = 'a' * 40
        run = {'repository': {'full_name': policy.REPOSITORY}, 'id': 10, 'run_attempt': 1,
               'head_sha': sha, 'event': event, 'path': '.github/workflows/validate.yml',
               'actor': {'login': actor}}
        environment = {'GITHUB_REPOSITORY': policy.REPOSITORY, 'GITHUB_RUN_ID': '10',
                       'GITHUB_RUN_ATTEMPT': '1', 'GITHUB_EVENT_NAME': event, 'GITHUB_SHA': sha}
        return run, environment

    def test_only_existing_exporter_policy_and_exact_canonical_provenance(self):
        run, environment = self.fixture()
        self.assertEqual(set(policy.SEATS), {'exporter'})
        self.assertEqual(policy.authorize(policy.SEATS['exporter'], run, environment)['run_id'], 10)
        for changed in (dict(run, event='schedule'), dict(run, path='.github/workflows/ci.yml'),
                        dict(run, repository={'full_name': 'VR-Vlog/VR-Vlog'})):
            with self.assertRaises(ValueError):
                policy.authorize(policy.SEATS['exporter'], changed, environment)
        run['actor'] = {'login': 'outsider'}
        with self.assertRaises(ValueError):
            policy.authorize(policy.SEATS['exporter'], run, environment)

    def test_external_pr_requires_native_exact_head_approval(self):
        run, environment = self.fixture('pull_request', 'outsider')
        pr = {'number': 12, 'state': 'open', 'base': {'repo': {'full_name': policy.REPOSITORY}},
              'head': {'sha': run['head_sha'], 'repo': {'full_name': 'outside/fork'}},
              'user': {'login': 'outsider'}, 'merge_commit_sha': 'b' * 40}
        approval = {'repository': policy.REPOSITORY, 'pr': 12, 'head_sha': run['head_sha'],
                    'approved_by': 'mitsuya0077', 'approval_input_id': 'discord-input-abc123'}
        with self.assertRaises(ValueError):
            policy.authorize(policy.SEATS['exporter'], run, environment, pr)
        self.assertEqual(policy.authorize(policy.SEATS['exporter'], run, environment, pr, [approval])['head_sha'], run['head_sha'])
        for changed in (dict(approval, head_sha='c' * 40), dict(approval, approved_by='outsider'),
                        dict(approval, repository='VR-Vlog/VR-Vlog-lilToon-Exporter'),
                        dict(approval, approval_input_id='freeform')):
            with self.assertRaises(ValueError):
                policy.authorize(policy.SEATS['exporter'], run, environment, pr, [changed])


class MigrationTests(unittest.TestCase):
    def test_service_loaded_or_unknown_is_never_treated_as_stopped(self):
        for result in (SimpleNamespace(returncode=0, stdout='', stderr=''),
                       SimpleNamespace(returncode=1, stdout='', stderr='Operation not permitted')):
            with mock.patch.object(updater.subprocess, 'run', return_value=result):
                with self.assertRaisesRegex(ValueError, 'EXPORTER_SERVICE_MUST_BE_UNLOADED'):
                    updater.unloaded()
        result = SimpleNamespace(returncode=113, stdout='', stderr='Could not find service ' + updater.LABEL)
        with mock.patch.object(updater.subprocess, 'run', return_value=result):
            updater.unloaded()

    def test_active_uid_stops_snapshot_before_configuration_or_mutation(self):
        with mock.patch.object(updater, 'unloaded'), \
             mock.patch.object(ci, 'verify_uid_idle', side_effect=ci.UnknownProcesses('CI_PROCESSES_REMAIN')), \
             mock.patch.object(updater, 'read') as read:
            with self.assertRaises(ci.UnknownProcesses):
                updater.snapshot()
            read.assert_not_called()

    def test_only_renamed_existing_role_can_migrate(self):
        config = {'seat_id': 'exporter', 'controller_uid': 501, 'controller_user': 'mouri',
                  'seat': json.loads(json.dumps(policy.SEATS['exporter']))}
        account = SimpleNamespace(pw_uid=402, pw_gid=402, pw_shell='/usr/bin/false', pw_dir=str(supervisor.RUNTIME / 'home'))
        with mock.patch.object(updater.pwd, 'getpwnam', return_value=account):
            for repository in policy.LEGACY_REPOSITORIES | {policy.REPOSITORY}:
                config['seat']['repository'] = repository
                self.assertEqual(updater.existing_seat(config)['repository'], policy.REPOSITORY)
            for bad in ('VR-Vlog/VR-Vlog', 'outside/fork'):
                config['seat']['repository'] = bad
                with self.assertRaises(ValueError):
                    updater.existing_seat(config)
            config['seat']['repository'] = policy.REPOSITORY
            config['seat']['label'] = 'new-runner-label'
            with self.assertRaises(ValueError):
                updater.existing_seat(config)

    def test_recovery_refuses_live_uid_before_any_github_call(self):
        with mock.patch.object(ci, 'verify_uid_idle', side_effect=ci.UnknownProcesses('CI_PROCESSES_REMAIN')), \
             mock.patch.object(supervisor, 'api') as api:
            with self.assertRaises(ci.UnknownProcesses):
                supervisor.reconcile({'seat_id': 'exporter'}, 402)
            api.assert_not_called()


if __name__ == '__main__':
    unittest.main()
