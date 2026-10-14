"""Immutable admission for the existing Exporter-only ephemeral runner."""
import re

REPOSITORY = 'VR-Vlog/VR-Vlog-Avatar-Exporter'
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
