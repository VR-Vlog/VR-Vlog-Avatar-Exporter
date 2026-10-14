"""Ask the root supervisor to authenticate and reserve this job before checkout."""
import json
import os
import socket
import sys

ADMISSION_SOCKET_TIMEOUT_SECONDS = 3660


def main():
    fields = ('GITHUB_REPOSITORY', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT',
              'GITHUB_EVENT_NAME', 'GITHUB_SHA')
    request = {key: os.environ.get(key, '') for key in fields}
    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as connection:
        connection.settimeout(ADMISSION_SOCKET_TIMEOUT_SECONDS)
        connection.connect(os.environ['EXPORTER_CI_ADMISSION_SOCKET'])
        connection.sendall(json.dumps(request).encode() + b'\n')
        data = bytearray()
        while not data.endswith(b'\n') and len(data) <= 16384:
            chunk = connection.recv(4096)
            if not chunk:
                break
            data.extend(chunk)
    if len(data) > 16384 or json.loads(data).get('ok') is not True:
        raise ValueError('CI admission rejected; no checkout is authorized')


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('Protected Mac CI admission failed', file=sys.stderr)
        raise SystemExit(1)
