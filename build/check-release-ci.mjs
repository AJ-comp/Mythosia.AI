// Require ordinary CI for the exact committed source before manual NuGet publication.
import { pathToFileURL } from 'node:url';

const workflowPath = '.github/workflows/ci.yml';
const pageSize = 100;
const maxPages = 10; // GitHub limits filtered workflow-run searches to 1,000 results.

function positiveInteger(value) {
  return Number.isSafeInteger(value) && value > 0;
}

export async function checkReleaseCi({
  repository,
  sha,
  token,
  apiUrl = 'https://api.github.com',
  fetchImpl = fetch,
}) {
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repository ?? '')) {
    throw new Error('GITHUB_REPOSITORY must identify one owner/repository.');
  }
  if (!/^(?:[0-9a-f]{40}|[0-9a-f]{64})$/i.test(sha ?? '')) {
    throw new Error('GITHUB_SHA must be the full release commit SHA.');
  }
  if (typeof token !== 'string' || !token.trim()) {
    throw new Error('GITHUB_TOKEN is required to verify release CI.');
  }
  const base = new URL(apiUrl);
  if (base.protocol !== 'https:' || base.username || base.password || base.search || base.hash) {
    throw new Error('GITHUB_API_URL must be an HTTPS API base URL without credentials, query, or fragment.');
  }
  sha = sha.toLowerCase();
  const apiRoot = `${base.href.replace(/\/$/, '')}/repos/${repository}/actions`;

  async function getJson(url) {
    let response;
    try {
      response = await fetchImpl(url, {
        headers: {
          Accept: 'application/vnd.github+json',
          Authorization: `Bearer ${token}`,
          'X-GitHub-Api-Version': '2022-11-28',
        },
        redirect: 'error',
        signal: AbortSignal.timeout(30_000),
      });
    } catch {
      throw new Error('GitHub CI verification request failed or timed out; publication is blocked.');
    }
    if (!response.ok) {
      throw new Error(`GitHub CI verification returned HTTP ${response.status}; publication is blocked.`);
    }
    try {
      return await response.json();
    } catch {
      throw new Error('GitHub CI verification returned invalid JSON; publication is blocked.');
    }
  }

  function validateRun(run) {
    if (!run || !positiveInteger(run.id) || !positiveInteger(run.workflow_id) ||
        !positiveInteger(run.run_number) || !positiveInteger(run.run_attempt) ||
        run.path !== workflowPath || run.event !== 'push' || run.head_branch !== 'main' ||
        typeof run.head_sha !== 'string' || run.head_sha.toLowerCase() !== sha ||
        typeof run.repository?.full_name !== 'string' ||
        run.repository.full_name.toLowerCase() !== repository.toLowerCase() ||
        typeof run.status !== 'string' || !run.status ||
        !(run.conclusion === null || typeof run.conclusion === 'string')) {
      throw new Error('GitHub returned an invalid or mismatched CI run; publication is blocked.');
    }
  }

  const runs = new Map();
  let totalCount;
  for (let page = 1; page <= maxPages; page++) {
    const url = new URL(`${apiRoot}/workflows/ci.yml/runs`);
    url.search = new URLSearchParams({
      branch: 'main',
      event: 'push',
      head_sha: sha,
      exclude_pull_requests: 'true',
      per_page: String(pageSize),
      page: String(page),
    }).toString();
    // Never filter by success: an earlier passing run must not hide a later failed run.
    const result = await getJson(url.href);
    if (!result || !Number.isSafeInteger(result.total_count) || result.total_count < 0 ||
        !Array.isArray(result.workflow_runs) || result.workflow_runs.length > pageSize) {
      throw new Error('GitHub returned an invalid CI run listing; publication is blocked.');
    }
    if (result.total_count > pageSize * maxPages) {
      throw new Error('CI run search exceeds the 1,000-run verification limit; publication is blocked.');
    }
    totalCount ??= result.total_count;
    if (result.total_count !== totalCount) {
      throw new Error('CI run listing changed during pagination; retry after CI is stable.');
    }
    for (const run of result.workflow_runs) {
      validateRun(run);
      if (runs.has(run.id)) {
        throw new Error('CI pagination returned duplicate runs; publication is blocked.');
      }
      runs.set(run.id, run);
    }
    if (runs.size === totalCount) break;
    if (runs.size > totalCount || result.workflow_runs.length !== pageSize || page === maxPages) {
      throw new Error('CI run listing is incomplete or inconsistent; publication is blocked.');
    }
  }
  if (runs.size === 0) {
    throw new Error(`No push/main CI run exists for release commit ${sha}. Wait for CI, then retry.`);
  }
  const latest = [...runs.values()].sort((left, right) => right.run_number - left.run_number)[0];
  if ([...runs.values()].some(run => run.id !== latest.id && run.run_number === latest.run_number)) {
    throw new Error('The latest CI run is ambiguous; publication is blocked.');
  }

  // Refresh the selected run, since a rerun may have started after the listing was read.
  // This endpoint reports the current run_attempt, not the previously successful attempt.
  const current = await getJson(`${apiRoot}/runs/${latest.id}`);
  validateRun(current);
  if (current.id !== latest.id || current.workflow_id !== latest.workflow_id ||
      current.run_number !== latest.run_number || current.run_attempt < latest.run_attempt) {
    throw new Error('CI run identity or attempt changed inconsistently; publication is blocked.');
  }
  if (current.status !== 'completed' || current.conclusion !== 'success') {
    throw new Error(`CI run ${current.id}, attempt ${current.run_attempt}, is ${current.status}/${current.conclusion ?? 'none'} for ${sha}; publication is blocked.`);
  }
  return {
    id: current.id,
    attempt: current.run_attempt,
    sha,
    status: current.status,
    conclusion: current.conclusion,
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const run = await checkReleaseCi({
      repository: process.env.GITHUB_REPOSITORY,
      sha: process.env.GITHUB_SHA,
      token: process.env.GITHUB_TOKEN,
      apiUrl: process.env.GITHUB_API_URL || 'https://api.github.com',
    });
    console.log(`Verified push/main CI run ${run.id}, attempt ${run.attempt}: ${run.status}/${run.conclusion} at ${run.sha}.`);
  } catch (error) {
    console.error(`Release CI check failed: ${error.message}`);
    process.exitCode = 1;
  }
}
