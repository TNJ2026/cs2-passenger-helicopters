import { existsSync, readdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const repository = fileURLToPath(new URL('..', import.meta.url));
const testDirectory = fileURLToPath(new URL('.', import.meta.url));

const intermediateRoots = [
  'assets_converted',
  'assets_game_import',
  'assets/helicopter/helipad',
  'assets/helicopter/heliport',
  'assets/helicopter/mil_mi26tc',
];

const assetPipelineTests = new Set([
  'helicopter-native-motion-blur.test.mjs',
  'helicopter-rotor-exposure.test.mjs',
  'helipad-lot.test.mjs',
  'helipad-pedestrian.test.mjs',
  'heliport-footprint-border.test.mjs',
  'heliport-width.test.mjs',
  'imported-heliport-layout.test.mjs',
  'mi26-rig.test.mjs',
]);

const stagedBuildTests = new Set([
  'helicopter-assembly-isolation.test.mjs',
]);

const hasIntermediateAssets = intermediateRoots.every((directory) =>
  existsSync(path.join(repository, directory)),
);

const hasStagedBuild = [
  'artifacts/staged/CityWeaver/CityWeaver.dll',
  'artifacts/staged/TransportFever3Helicopters.Runtime/TransportFever3Helicopters.Runtime.dll',
  'artifacts/staged/PassengerHelicopters/PassengerHelicopters.dll',
].every((file) => existsSync(path.join(repository, file)));

const testFiles = readdirSync(testDirectory)
  .filter((file) => file.endsWith('.test.mjs'))
  .filter((file) => hasIntermediateAssets || !assetPipelineTests.has(file))
  .filter((file) => hasStagedBuild || !stagedBuildTests.has(file))
  .sort()
  .map((file) => path.join('test', file));

if (!hasIntermediateAssets) {
  console.log(
    `Intermediate assets are not present; skipping ${assetPipelineTests.size} asset-pipeline tests.`,
  );
}

if (!hasStagedBuild) {
  console.log(
    `Staged assemblies are not present; skipping ${stagedBuildTests.size} build-artifact test.`,
  );
}

const result = spawnSync(
  process.execPath,
  ['--test', '--test-concurrency=2', ...testFiles],
  { cwd: repository, stdio: 'inherit' },
);

if (result.error) throw result.error;
process.exitCode = result.status ?? 1;
