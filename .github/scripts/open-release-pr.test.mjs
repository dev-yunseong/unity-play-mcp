import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { openReleasePr } from './open-release-pr.mjs';

const options = { repository: 'owner/repo', branch: 'bump-version/unity-0.5.3',
  title: 'chore: release `literal` $(literal)', actor: 'owner', sha: 'a'.repeat(40) };

function fixture({ existing = false, createError, dispatchError, head = options.sha, base = 'develop', state = 'OPEN' } = {}) {
  const calls = [];
  let bodyPath;
  const run = (command, args) => {
    assert.equal(command, 'gh');
    calls.push(args);
    switch (args.slice(0, 2).join(' ')) {
      case 'pr list': return JSON.stringify(existing ? [{ number: 42 }] : []);
      case 'pr create':
        bodyPath = args[args.indexOf('--body-file') + 1];
        assert.match(readFileSync(bodyPath, 'utf8'), /dispatch request is not a passing test result/);
        assert.equal(args[args.indexOf('--title') + 1], options.title);
        assert.ok(args.includes('--draft'));
        if (createError) throw new Error(createError);
        return 'https://github.com/owner/repo/pull/42';
      case 'pr view': return JSON.stringify({ url: 'https://github.com/owner/repo/pull/42', headRefOid: head, baseRefName: base, state });
      case 'workflow run':
        if (dispatchError) throw new Error(dispatchError);
        return '';
      default: throw new Error(`Unexpected call: ${args}`);
    }
  };
  return { run, calls, get bodyPath() { return bodyPath; } };
}

for (const existing of [false, true]) {
  test(`${existing ? 'existing' : 'new'} PR dispatches CI for the full pushed SHA`, () => {
    const f = fixture({ existing });
    assert.match(openReleasePr(options, f.run), /verify the completed results/);
    assert.deepEqual(f.calls.at(-1), ['workflow', 'run', 'unity-tests.yml', '--repo', options.repository,
      '--ref', options.branch, '-f', `expected_sha=${options.sha}`]);
    assert.equal(f.calls.some(args => args[1] === 'create'), !existing);
    if (!existing) assert.equal(existsSync(f.bodyPath), false);
  });
}

test('denied creation fails with recovery instructions and never dispatches', () => {
  const f = fixture({ createError: 'GitHub Actions is not permitted to create or approve pull requests' });
  assert.throws(() => openReleasePr(options, f.run), /Release PR creation failed:[\s\S]*compare\/develop[\s\S]*gh pr create[\s\S]*expected_sha=/);
  assert.equal(f.calls.some(args => args[0] === 'workflow'), false);
  assert.equal(existsSync(f.bodyPath), false);
});

for (const mismatch of [{ head: 'b'.repeat(40) }, { base: 'other' }, { state: 'MERGED' }]) {
  test(`rejects a changed PR: ${JSON.stringify(mismatch)}`, () => {
    const f = fixture({ existing: true, ...mismatch });
    assert.throws(() => openReleasePr(options, f.run), /no longer matches/);
    assert.equal(f.calls.some(args => args[0] === 'workflow'), false);
  });
}

test('dispatch failure remains a failure with explicit retry command', () => {
  const f = fixture({ existing: true, dispatchError: 'HTTP 403' });
  assert.throws(() => openReleasePr(options, f.run), /Unity CI dispatch failed: HTTP 403[\s\S]*gh workflow run/);
});

test('invalid SHA and branch fail before any command runs', () => {
  const f = fixture();
  assert.throws(() => openReleasePr({ ...options, sha: 'abc' }, f.run), /full HEAD SHA/);
  assert.throws(() => openReleasePr({ ...options, branch: 'main;danger' }, f.run), /release branch/);
  assert.equal(f.calls.length, 0);
});
