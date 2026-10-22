import { test } from 'node:test';
import assert from 'node:assert/strict';
import { csprojVersion, nextVersion, npmName } from './resolve-version.mjs';

test('the package.json version is used while it is not published', () => {
  assert.equal(nextVersion('0.1.0', []), '0.1.0');
  assert.equal(nextVersion('0.2.0', ['0.1.0', '0.1.0-build.3']), '0.2.0');
});

test('a published version gets the next build number', () => {
  assert.equal(nextVersion('0.1.0', ['0.1.0']), '0.1.0-build.1');
  assert.equal(nextVersion('0.1.0', ['0.1.0', '0.1.0-build.1', '0.1.0-build.2']), '0.1.0-build.3');
  assert.equal(nextVersion('0.1.0', ['0.1.0', '0.1.0-build.9', '0.1.0-build.10', '0.10.0-build.40']), '0.1.0-build.11');
});

test('the npm scope is the repository owner, as GitHub Packages requires', () => {
  assert.equal(npmName('@certifie/web3-service', 'certifie'), '@certifie/web3-service');
  assert.equal(npmName('@certifie/web3-service', 'LorenzoColautti'), '@lorenzocolautti/web3-service');
});

test('the NuGet version is the <Version> of the .csproj', () => {
  assert.equal(csprojVersion('<Project><PropertyGroup>\n    <Version>0.2.0</Version>\n  </PropertyGroup></Project>'), '0.2.0');
  assert.throws(() => csprojVersion('<Project />'));
});
