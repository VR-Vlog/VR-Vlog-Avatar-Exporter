"""Immutable admission for the existing Exporter-only ephemeral runner."""
import re

REPOSITORY = 'VR-Vlog/VR-Vlog-Avatar-Exporter'
REPOSITORY_ID = 1355769104
LEGACY_REPOSITORIES = frozenset(('mitsuya0077/VR-Vlog-lilToon-Exporter', 'VR-Vlog/VR-Vlog-lilToon-Exporter'))
ACTORS = frozenset(('mitsuya0077', 'mkc1370', 'mirukudasi'))
SEATS = {
    'exporter': {'repository': REPOSITORY, 'name': 'exporter-general-mac-mini',
                 'label': 'exporter-general-mac-arm64', 'user': '_mysi_ci_exporter',
                 'workflows': {
                     'validate.yml': ('push', 'pull_request'),
                     'build-listing.yml': ('pull_request', 'release', 'workflow_dispatch'),
                     'release-candidate.yml': ('workflow_dispatch',),
                     'release-vpm.yml': ('workflow_dispatch',),
                     'check-upstream.yml': ('workflow_dispatch',),
                 }},
}


def positive(value):
    return type(value) is int and value > 0


def authorize(seat, run, environment, pr=None, approvals=()):
    """Authenticate GitHub API provenance and exact-head protected approval."""
    repository = seat['repository']
    if (seat != SEATS['exporter'] or not isinstance(run, dict)
            or run.get('repository', {}).get('full_name') != repository
            or not positive(run.get('id')) or not positive(run.get('run_attempt'))
            or str(run['id']) != environment.get('GITHUB_RUN_ID')
            or str(run['run_attempt']) != environment.get('GITHUB_RUN_ATTEMPT')
            or environment.get('GITHUB_REPOSITORY') != repository
            or not re.fullmatch(r'[0-9a-f]{40}', run.get('head_sha', ''))
            or run.get('event') != environment.get('GITHUB_EVENT_NAME')):
        raise ValueError('Run identity is not authentic')
    path = run.get('path', '').partition('@')[0]
    workflow = path.removeprefix('.github/workflows/')
    if path != '.github/workflows/' + workflow or run['event'] not in seat['workflows'].get(workflow, ()):
        raise ValueError('Workflow/event is outside the approved seat')
    actor = run.get('actor', {}).get('login')
    triggering = run.get('triggering_actor', run.get('actor', {})).get('login')
    if actor not in ACTORS or triggering not in ACTORS:
        if run['event'] != 'pull_request':
            raise ValueError('Run actor is not approved')
    if run['event'] == 'pull_request':
        if (not isinstance(pr, dict) or pr.get('base', {}).get('repo', {}).get('full_name') != repository
                or not positive(pr.get('number')) or pr.get('state') != 'open'
                or run.get('head_sha') != pr.get('head', {}).get('sha')):
            raise ValueError('PR head changed or is outside the repository')
        internal = (pr.get('head', {}).get('repo', {}).get('full_name') == repository
                    and pr.get('user', {}).get('login') in ACTORS
                    and actor in ACTORS and triggering in ACTORS)
        approved = any(isinstance(a, dict) and a.get('repository') == repository
                       and a.get('pr') == pr['number'] and a.get('head_sha') == pr['head']['sha']
                       and a.get('approved_by') == 'mitsuya0077'
                       and re.fullmatch(r'discord-input-[0-9a-f]+', a.get('approval_input_id', ''))
                       for a in approvals)
        if not internal and not approved:
            raise ValueError('External PR needs native protected approval of this exact head')
        if environment.get('GITHUB_SHA') not in (pr['head']['sha'], pr.get('merge_commit_sha')):
            raise ValueError('PR checkout revision is not authentic')
    elif environment.get('GITHUB_SHA') != run['head_sha']:
        raise ValueError('Checkout revision differs from the run')
    return {'repository': repository, 'run_id': run['id'], 'run_attempt': run['run_attempt'],
            'head_sha': run['head_sha'], 'checkout_sha': environment['GITHUB_SHA'], 'workflow': path}


def validate_registry(registry, *, allow_legacy=False):
    """Require complete root-written registration history; never accept labels alone."""
    repositories = LEGACY_REPOSITORIES | {REPOSITORY} if allow_legacy else {REPOSITORY}
    if (not isinstance(registry, dict)
            or set(registry) != {'schema', 'repository', 'source_sha', 'approval_input_id', 'registrations'}
            or type(registry.get('schema')) is not int or registry['schema'] != 1
            or registry.get('repository') not in repositories
            or not isinstance(registry.get('source_sha'), str)
            or not re.fullmatch('[0-9a-f]{40}', registry['source_sha'])
            or not isinstance(registry.get('approval_input_id'), str)
            or not re.fullmatch('discord-input-[0-9a-f]+', registry['approval_input_id'])
            or not isinstance(registry.get('registrations'), list)
            or not 0 <= len(registry['registrations']) <= 10000):
        raise ValueError('Exporter registry provenance is incomplete')
    for record in registry['registrations']:
        if (not isinstance(record, dict) or set(record) != {'id', 'name', 'labels', 'group_id', 'group_name'}
                or not positive(record.get('id')) or not positive(record.get('group_id'))
                or not isinstance(record.get('name'), str)
                or not re.fullmatch('exporter-general-mac-mini-[0-9a-f]{32}', record['name'])
                or record.get('labels') != ['exporter-general-mac-arm64']
                or not isinstance(record.get('group_name'), str)
                or not record['group_name'].strip() or record['group_name'] != record['group_name'].strip()
                or len(record['group_name']) > 512 or any(ord(c) < 32 for c in record['group_name'])):
            raise ValueError('Exporter registration history is not authentic')
    if len({record['id'] for record in registry['registrations']}) != len(registry['registrations']):
        raise ValueError('Exporter registration history is ambiguous')
    return registry
