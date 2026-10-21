#!/usr/bin/env python3
"""Actual HTTP checks against the API and PostgreSQL-backed UserService.
Run against a development instance; creates one unique test user.
"""
import base64
import concurrent.futures
import hashlib
import hmac
import json
import os
import time
import urllib.error
import urllib.request
import uuid

base = os.environ.get('API_BASE_URL', 'http://127.0.0.1:5080')
checks = 0

def call(method, path, expected, body=None, token=None):
    global checks
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    req = urllib.request.Request(base + path, data=json.dumps(body).encode() if body is not None else None,
                                 headers=headers, method=method)
    try:
        response = urllib.request.urlopen(req)
    except urllib.error.HTTPError as error:
        response = error
    raw = response.read()
    assert response.status in (expected if isinstance(expected, tuple) else (expected,)), f'{method} {path}: expected {expected}, got {response.status}'
    checks += 1
    return json.loads(raw) if raw else None

email = f'smoke-{uuid.uuid4().hex}@example.com'
credentials = {'email': email, 'password': 'Learning-JWT!2026-Long'}
spec = call('GET', '/swagger/v1/swagger.json', 200)
assert len(spec['paths']) == 5
assert 'Bearer' in spec['components']['securitySchemes']
call('GET', '/api/users/me', 401)
user = call('POST', '/api/users/register', 201, credentials)
call('POST', '/api/users/register', 409, credentials)
call('POST', '/api/users/register', 400, {'email': 'invalid', 'password': 'short'})
call('POST', '/api/users/login', 401, {**credentials, 'password': 'wrong'})
call('POST', '/api/users/login', 401, {**credentials, 'email': 'unknown@example.com'})
tokens = call('POST', '/api/users/login', 200, credentials)
me = call('GET', '/api/users/me', 200, token=tokens['accessToken'])
assert me == user
assert set(me) == {'id', 'email'}
parts = tokens['accessToken'].split('.')
payload = json.loads(base64.urlsafe_b64decode(parts[1] + '=' * (-len(parts[1]) % 4)))
assert payload['sub'] == user['id'] and payload['jti']
assert payload['exp'] - payload['iat'] == 600
assert 'password' not in payload and 'email' not in payload

def signed(claims):
    key = os.environ['Jwt__SigningKey'].encode()
    b64 = lambda data: base64.urlsafe_b64encode(data).rstrip(b'=').decode()
    content = parts[0] + '.' + b64(json.dumps(claims).encode())
    return content + '.' + b64(hmac.new(key, content.encode(), hashlib.sha256).digest())

call('GET', '/api/users/me', 401, token=parts[0] + '.' + parts[1] + '.invalid')
call('GET', '/api/users/me', 401, token=signed({**payload, 'exp': int(time.time()) - 120}))
call('GET', '/api/users/me', 401, token=signed({**payload, 'iss': 'wrong-issuer'}))
call('GET', '/api/users/me', 401, token=signed({**payload, 'aud': 'wrong-audience'}))
rotated = call('POST', '/api/users/refresh', 200, {'refreshToken': tokens['refreshToken']})
assert rotated['refreshToken'] != tokens['refreshToken']
call('POST', '/api/users/refresh', 401, {'refreshToken': tokens['refreshToken']})
call('POST', '/api/users/refresh', 401, {'refreshToken': rotated['refreshToken']})
second = call('POST', '/api/users/login', 200, credentials)
call('POST', '/api/users/logout', 204, {'refreshToken': second['refreshToken']})
call('POST', '/api/users/refresh', 401, {'refreshToken': second['refreshToken']})
# Logout revokes refresh tokens; existing access tokens live until their short expiry.
call('GET', '/api/users/me', 200, token=second['accessToken'])
concurrent_tokens = call('POST', '/api/users/login', 200, credentials)
def rotate():
    return call('POST', '/api/users/refresh', (200, 401), {'refreshToken': concurrent_tokens['refreshToken']})
with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
    results = list(pool.map(lambda _: rotate(), range(2)))
winners = [x for x in results if 'accessToken' in x]
assert len(winners) == 1, 'Concurrent refresh must have exactly one winner'
call('POST', '/api/users/refresh', 401, {'refreshToken': winners[0]['refreshToken']})
print(f'PASS: {checks} HTTP checks; register/login/JWT validation/rotation/reuse detection/concurrency/logout/Swagger.')
