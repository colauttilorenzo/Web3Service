// Prints the version to publish: the one in typescript/package.json, or the same version with a new build number
// (0.1.0-build.1, 0.1.0-build.2, …) when npm or NuGet on GitHub Packages already has it.
// GitHub Packages requires the npm scope to be the repository owner, so the npm name is @<owner>/web3-service.
// Usage: node .github/scripts/resolve-version.mjs  (env: GITHUB_TOKEN, GITHUB_REPOSITORY_OWNER, GITHUB_OUTPUT)
import { appendFileSync, readFileSync } from 'node:fs';

const BUILD = /^(.+)-build\.(\d+)$/;

/** The npm name on GitHub Packages: the package.json name with the owner as scope, e.g. @certifie/web3-service. */
export function npmName(name, owner) {
  return `@${owner.toLowerCase()}/${name.split('/').pop()}`;
}

/** The next free version: the base version, or base-build.N with N one above the highest build already published. */
export function nextVersion(base, published) {
  const existing = new Set(published.map(version => version.toLowerCase()));
  if (!existing.has(base.toLowerCase())) return base;
  const builds = [...existing].map(version => BUILD.exec(version)).filter(match => match?.[1] === base.toLowerCase());
  return `${base}-build.${Math.max(0, ...builds.map(match => Number(match[2]))) + 1}`;
}

/** Versions of the npm package on GitHub Packages; none when the package was never published. */
async function npmVersions(name, token) {
  const response = await fetch(`https://npm.pkg.github.com/${name.replace('/', '%2f')}`, { headers: { authorization: `Bearer ${token}` } });
  if (response.status === 404) return [];
  if (!response.ok) throw new Error(`npm registry answered ${response.status} for ${name}`);
  return Object.keys((await response.json()).versions ?? {});
}

/** Versions of the NuGet package on GitHub Packages; none when the package was never published. */
async function nugetVersions(owner, id, token) {
  const url = `https://nuget.pkg.github.com/${owner}/download/${id.toLowerCase()}/index.json`;
  const response = await fetch(url, { headers: { authorization: `Basic ${Buffer.from(`${owner}:${token}`).toString('base64')}` } });
  if (response.status === 404) return [];
  if (!response.ok) throw new Error(`NuGet feed answered ${response.status} for ${id}`);
  return (await response.json()).versions ?? [];
}

async function main() {
  const { name, version } = JSON.parse(readFileSync(new URL('../../typescript/package.json', import.meta.url), 'utf8'));
  const { GITHUB_TOKEN: token, GITHUB_REPOSITORY_OWNER: owner, GITHUB_OUTPUT: output } = process.env;
  const npm = npmName(name, owner);
  const published = [...await npmVersions(npm, token), ...await nugetVersions(owner, 'Certifie.Web3Service', token)];
  const resolved = nextVersion(version, published);
  console.log(`package.json ${version}, publishing ${npm}@${resolved} and Certifie.Web3Service ${resolved}`);
  if (output) appendFileSync(output, `version=${resolved}\nnpm-name=${npm}\n`);
}

if (import.meta.url === `file://${process.argv[1]}`) await main();
