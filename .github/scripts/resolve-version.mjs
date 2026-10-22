// Prints the version to publish for one package: the one in its project file, or the same version with a new build number
// (0.1.0-build.1, 0.1.0-build.2, …) when that package on GitHub Packages already has it.
// npm reads typescript/package.json, NuGet reads <Version> in dotnet/src/Certifie.Web3Service/Certifie.Web3Service.csproj.
// GitHub Packages requires the npm scope to be the repository owner, so the npm name is @<owner>/web3-service.
// Usage: node .github/scripts/resolve-version.mjs npm|nuget  (env: GITHUB_TOKEN, GITHUB_REPOSITORY_OWNER, GITHUB_OUTPUT)
import { appendFileSync, readFileSync } from 'node:fs';

const BUILD = /^(.+)-build\.(\d+)$/;
const NUGET_ID = 'Certifie.Web3Service';

/** The npm name on GitHub Packages: the package.json name with the owner as scope, e.g. @certifie/web3-service. */
export function npmName(name, owner) {
  return `@${owner.toLowerCase()}/${name.split('/').pop()}`;
}

/** The <Version> of a .csproj. */
export function csprojVersion(xml) {
  const match = /<Version>\s*([^<\s]+)\s*<\/Version>/.exec(xml);
  if (!match) throw new Error('No <Version> in the .csproj');
  return match[1];
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

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8');

async function resolveNpm(owner, token) {
  const { name, version } = JSON.parse(read('../../typescript/package.json'));
  const npm = npmName(name, owner);
  return { name: npm, base: version, version: nextVersion(version, await npmVersions(npm, token)) };
}

async function resolveNuget(owner, token) {
  const version = csprojVersion(read(`../../dotnet/src/${NUGET_ID}/${NUGET_ID}.csproj`));
  return { name: NUGET_ID, base: version, version: nextVersion(version, await nugetVersions(owner, NUGET_ID, token)) };
}

async function main() {
  const resolvers = { npm: resolveNpm, nuget: resolveNuget };
  const kind = process.argv[2];
  if (!resolvers[kind]) throw new Error('Usage: resolve-version.mjs npm|nuget');
  const { GITHUB_TOKEN: token, GITHUB_REPOSITORY_OWNER: owner, GITHUB_OUTPUT: output } = process.env;
  const { name, base, version } = await resolvers[kind](owner, token);
  console.log(`${kind} ${name}: project version ${base}, publishing ${version}`);
  if (output) appendFileSync(output, `version=${version}\nname=${name}\nprerelease=${version !== base}\n`);
}

if (import.meta.url === `file://${process.argv[1]}`) await main();
