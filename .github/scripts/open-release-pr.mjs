import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const execute = (command, args) => execFileSync(command, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();

export function openReleasePr({ repository, branch, title, actor, sha }, run = execute) {
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository ?? '') ||
      !/^bump-version\/[a-zA-Z0-9./-]+$/.test(branch ?? '') ||
      !/^[a-f0-9]{40}$/.test(sha ?? '') || !actor || !title) {
    throw new Error('repository, release branch, title, actor and full HEAD SHA are required');
  }
  const recovery = `Branch remains available: https://github.com/${repository}/compare/develop...${branch}\n` +
    `After checking repository Actions PR permissions, rerun the bump workflow or create a draft manually:\n` +
    `gh pr create --repo ${repository} --base develop --head ${branch} --draft --fill\n` +
    `Then run: gh workflow run unity-tests.yml --repo ${repository} --ref ${branch} -f expected_sha=${sha}\n` +
    'Wait for both Unity test jobs on this SHA to pass before merging. Dispatch acceptance is not test success.';
  const gh = (...args) => run('gh', args);
  const prs = JSON.parse(gh('pr', 'list', '--repo', repository, '--base', 'develop', '--head', branch, '--state', 'open', '--json', 'number'));
  if (prs.length > 1) throw new Error(`Multiple open release PRs found.\n${recovery}`);
  let number = prs[0]?.number;
  if (!number) {
    const directory = mkdtempSync(join(tmpdir(), 'release-pr-'));
    const bodyFile = join(directory, 'body.md');
    try {
      writeFileSync(bodyFile, `## Why\n\nPrepare the release version update.\n\n## What Changed\n\n${title}\n\n## Validation\n\nTag/npm availability and the version diff were checked by the bump workflow. Unity CI is explicitly dispatched for commit ${sha} after PR creation. A dispatch request is not a passing test result; check both EditMode and PlayMode results before merging.\n\n## Next steps\n\nReview the diff and tests, mark the draft ready, then merge into develop. Create the release from the merged commit separately.\n`, 'utf8');
      // shell 을 사용하지 않아 title 과 body 의 특수문자가 실행되지 않는다.
      gh('pr', 'create', '--repo', repository, '--draft', '--base', 'develop', '--head', branch,
        '--title', title, '--label', 'chore', '--assignee', actor, '--body-file', bodyFile);
    } catch (error) {
      throw new Error(`Release PR creation failed: ${error.message}\n${recovery}`);
    } finally {
      rmSync(directory, { recursive: true, force: true });
    }
    number = branch;
  }
  const pr = JSON.parse(gh('pr', 'view', String(number), '--repo', repository, '--json', 'url,headRefOid,baseRefName,state'));
  if (pr.headRefOid !== sha || pr.baseRefName !== 'develop' || pr.state !== 'OPEN') {
    throw new Error(`Release PR no longer matches the pushed HEAD ${sha}.\n${recovery}`);
  }
  try {
    gh('workflow', 'run', 'unity-tests.yml', '--repo', repository, '--ref', branch, '-f', `expected_sha=${sha}`);
  } catch (error) {
    throw new Error(`Unity CI dispatch failed: ${error.message}\n${recovery}`);
  }
  return `${pr.url}\nUnity CI requested for ${sha}; verify the completed results before merging.`;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    console.log(openReleasePr({ repository: process.env.GITHUB_REPOSITORY, branch: process.argv[2],
      title: process.argv[3], actor: process.env.DISPATCH_ACTOR, sha: execute('git', ['rev-parse', 'HEAD']) }));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
