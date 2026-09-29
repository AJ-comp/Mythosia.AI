// Run with: node --test build/test-release-ci.mjs
// All GitHub responses are fixtures; these tests make no network calls.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { checkReleaseCi } from './check-release-ci.mjs';

const sha = 'a'.repeat(40);
const repository = 'AJ-comp/Mythosia.AI';
const token = 'test-only-token';
const apiRoot = `https://api.github.com/repos/${repository}/actions`;

function run(overrides = {}) {
  return {
    id: 20, workflow_id: 7, run_number: 20, run_attempt: 1,
    path: '.github/workflows/ci.yml', event: 'push', head_branch: 'main', head_sha: sha,
    repository: { full_name: repository }, status: 'completed', conclusion: 'success',
    ...overrides,
  };
}

function listing(runs, totalCount = runs.length) {
  return { total_count: totalCount, workflow_runs: runs };
}

function mockFetch(responses) {
  const calls = [];
  return {
    calls,
    fetchImpl: async (url, options) => {
      calls.push({ url: new URL(url), options });
      assert.ok(responses.length, 'Unexpected extra GitHub request.');
      const response = responses.shift();
      if (response instanceof Error) throw response;
      return response?.httpResponse ?? { ok: true, json: async () => response };
    },
  };
}

async function check(responses, overrides = {}) {
  const mock = mockFetch(responses);
  const result = await checkReleaseCi({ repository, sha, token, ...mock, ...overrides });
  assert.equal(responses.length, 0, 'Every expected GitHub request must execute.');
  return { result, ...mock };
}

test('checks exact workflow, event, branch and SHA, then refreshes the latest attempt', async () => {
  const { result, calls } = await check([listing([run()]), run({ run_attempt: 2 })]);
  assert.deepEqual(result, { id: 20, attempt: 2, sha, status: 'completed', conclusion: 'success' });
  assert.equal(calls[0].url.pathname, `/repos/${repository}/actions/workflows/ci.yml/runs`);
  assert.deepEqual(Object.fromEntries(calls[0].url.searchParams), {
    branch: 'main', event: 'push', head_sha: sha, exclude_pull_requests: 'true', per_page: '100', page: '1',
  });
  assert.equal(calls[1].url.href, `${apiRoot}/runs/20`);
  for (const { options } of calls) {
    assert.equal(options.headers.Authorization, `Bearer ${token}`);
    assert.equal(options.redirect, 'error');
    assert.ok(options.signal instanceof AbortSignal);
  }
});

test('a later failure blocks publication despite an older successful run', async () => {
  const failed = run({ id: 21, run_number: 21, conclusion: 'failure' });
  await assert.rejects(check([listing([run(), failed]), failed]), /attempt 1.*completed\/failure/);
});

test('a newer attempt starting after the listing blocks a stale success', async () => {
  await assert.rejects(check([
    listing([run()]), run({ run_attempt: 2, status: 'in_progress', conclusion: null }),
  ]), /attempt 2.*in_progress\/none/);
});

test('only completed/success qualifies, including cancelled and skipped reruns', async () => {
  for (const [status, conclusion] of [
    ['queued', null], ['waiting', null], ['completed', 'cancelled'],
    ['completed', 'skipped'], ['completed', 'neutral'], ['completed', 'timed_out'],
  ]) {
    await assert.rejects(check([listing([run()]), run({ status, conclusion })]), /publication is blocked/);
  }
});

test('searches every page and chooses the latest run regardless of response order', async () => {
  const firstPage = Array.from({ length: 100 }, (_, index) => run({ id: index + 1, run_number: index + 1 }));
  const newest = run({ id: 101, run_number: 101 });
  const { calls, result } = await check([listing(firstPage, 101), listing([newest], 101), newest]);
  assert.equal(calls[1].url.searchParams.get('page'), '2');
  assert.equal(result.id, 101);
});

test('rejects missing, truncated, duplicate, changing and oversized listings', async () => {
  const fullPage = Array.from({ length: 100 }, (_, index) => run({ id: index + 1, run_number: index + 1 }));
  const cases = [
    [[listing([])], /No push\/main CI run/],
    [[listing([run()], 2)], /incomplete/],
    [[listing([run(), run()])], /duplicate/],
    [[listing([], 1001)], /1,000-run verification limit/],
    [[listing(fullPage, 101), listing([run({ id: 101, run_number: 101 })], 102)], /changed during pagination/],
    [[listing([run({ id: 19 }), run()])], /ambiguous/],
  ];
  for (const [responses, error] of cases) await assert.rejects(check(responses), error);
});

test('rejects a mismatched repository, source, workflow, trigger, or malformed identity', async () => {
  for (const overrides of [
    { repository: { full_name: 'someone/fork' } }, { head_sha: 'b'.repeat(40) },
    { path: '.github/workflows/publish-nuget.yml' }, { event: 'pull_request' },
    { head_branch: 'codex/task' }, { run_attempt: 0 }, { id: '20' }, { status: undefined },
    { head_sha: 123 }, { repository: { full_name: 123 } },
  ]) {
    await assert.rejects(check([listing([run(overrides)])]), /invalid or mismatched/);
    await assert.rejects(check([listing([run()]), run(overrides)]), /invalid or mismatched/);
  }
});

test('rejects detail responses that change the run identity or regress the attempt', async () => {
  for (const overrides of [{ id: 21 }, { workflow_id: 8 }, { run_number: 21 }, { run_attempt: 1 }]) {
    await assert.rejects(check([listing([run({ run_attempt: 2 })]), run(overrides)]), /identity or attempt/);
  }
});

test('network, HTTP, and JSON errors fail closed without exposing response bodies', async () => {
  const errors = [
    new Error(`network failure ${token}`),
    { httpResponse: { ok: false, status: 403, json: async () => ({ message: token }) } },
    { httpResponse: { ok: true, json: async () => { throw new Error(token); } } },
  ];
  for (const response of errors) {
    for (const responses of [[response], [listing([run()]), response]]) {
      await assert.rejects(check(responses), error => {
        assert.match(error.message, /publication is blocked/);
        assert.ok(!error.message.includes(token));
        return true;
      });
    }
  }
});

test('rejects malformed listings and missing configuration before accepting CI', async () => {
  for (const response of [null, {}, { total_count: '1', workflow_runs: [run()] }, listing([run()], -1)]) {
    await assert.rejects(check([response]), /invalid CI run listing/);
  }
  for (const overrides of [
    { token: '' }, { repository: '../owner/repo' }, { sha: 'main' }, { sha: 'a'.repeat(41) },
    { apiUrl: 'http://api.github.com' }, { apiUrl: 'https://user:password@api.github.com' },
  ]) {
    await assert.rejects(check([], overrides));
  }
});

test('the release workflow checks CI in both jobs before publication and pins checkouts', () => {
  const workflow = readFileSync(new URL('../.github/workflows/publish-nuget.yml', import.meta.url), 'utf8').replace(/\r\n/g, '\n');
  const [validate, publish] = workflow.split('\n  publish:\n');
  assert.ok(publish, 'The secret-bearing publish job must remain separate.');
  assert.match(workflow, /permissions:\s*\n\s+contents: read\s*\n\s+actions: read/);
  for (const job of [validate, publish]) {
    assert.equal((job.match(/run: node build\/check-release-ci\.mjs/g) ?? []).length, 1);
    assert.match(job, /ref: \$\{\{ github.sha \}\}/);
    assert.match(job, /persist-credentials: false/);
    assert.match(job, /GITHUB_TOKEN: \$\{\{ github.token \}\}/);
  }
  assert.ok(validate.indexOf('node build/check-release-ci.mjs') < validate.indexOf('name: Restore'));
  assert.ok(publish.indexOf('node build/check-release-ci.mjs') < publish.indexOf('NUGET_API_KEY:'));
  assert.match(publish, /if: \$\{\{ inputs.publish \}\}/);
  assert.match(publish, /environment: nuget-production/);
});
