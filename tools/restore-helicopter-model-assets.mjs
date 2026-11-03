import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {fileURLToPath, pathToFileURL} from 'node:url';

const workspace = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export function inside(root, file) {
  const relative = path.relative(root, file);
  return relative !== '' && !relative.startsWith('..') && !path.isAbsolute(relative);
}
export function selectDependencies(assets, names) {
  const byId = new Map(assets.map(asset => [asset.id, asset]));
  const queue = names.map(name => {
    const matches = assets.filter(asset => path.basename(asset.path) === name + '.Prefab');
    if (matches.length !== 1) throw Error('Expected one backed-up prefab: ' + name);
    return matches[0].id;
  });
  const selected = new Set();
  while (queue.length) {
    const id = queue.pop();
    if (selected.has(id)) continue;
    const asset = byId.get(id);
    if (!asset) throw Error('Dependency missing from backup plan: ' + id);
    selected.add(id);
    queue.push(...asset.refs);
  }
  return assets.filter(asset => selected.has(asset.id));
}
async function sha(file) {
  const hash = crypto.createHash('sha256');
  for await (const chunk of fs.createReadStream(file)) hash.update(chunk);
  return hash.digest('hex');
}
function regularFile(root, file) {
  if (!inside(root, file)) throw Error('File escaped root: ' + file);
  for (let parent = file; parent !== root; parent = path.dirname(parent)) {
    if (fs.existsSync(parent) && fs.lstatSync(parent).isSymbolicLink()) throw Error('Linked path: ' + parent);
  }
  if (!fs.statSync(file).isFile()) throw Error('Expected regular file: ' + file);
}
function closed() {
  execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command',
    'if(Get-Process Cities2 -ErrorAction SilentlyContinue){exit 42}; exit 0'], {stdio:'pipe'});
}
async function main() {
  const plan = JSON.parse(fs.readFileSync(path.join(workspace, 'artifacts/helicopter-model-removal-plan.json')));
  const removal = JSON.parse(fs.readFileSync(path.join(workspace, 'artifacts/helicopter-model-removal-result.json')));
  const backup = path.resolve(removal.backup);
  if (!inside(path.join(workspace, 'artifacts/backups'), backup)) throw Error('Unexpected backup directory');
  const manifest = JSON.parse(fs.readFileSync(path.join(backup, 'restore-manifest.json')));
  const game = path.resolve('C:/Users/sc/AppData/LocalLow/Colossal Order/Cities Skylines II');
  const destination = path.join(game, 'ImportedData');
  if (path.resolve(plan.summary.root) !== destination) throw Error('Unexpected game asset root');
  const source = fs.readFileSync(path.join(workspace, 'Runtime/Transport/ImportedHeliportFootprintSystem.cs'), 'utf8');
  const defaults = JSON.parse(fs.readFileSync(path.join(workspace, 'assets/vehicle-defaults/mi26.json')));
  const names = [
    'CityWeaver_850b7b8e4f8b4a679fa2623a7c3dd3e0',
    'CityWeaver_7e149f3fb9574d47adfac7977c83d900',
    'CityWeaver_4a7fc21e03ce4e449bc02ae964c6ffe2',
    ...defaults.parts.map(part => part.render_prefab),
    ...[...source.matchAll(/"(CityWeaver_[^"]+ Mesh)"/g)].map(match => match[1]),
  ];
  const selected = selectDependencies(plan.assets, [...new Set(names)]);
  const records = new Map(manifest.files.map(file => [path.resolve(file.source), file]));
  const files = selected.flatMap(asset => [asset.path, asset.path + '.cid']).map(source => {
    const record = records.get(path.resolve(source));
    if (!record) throw Error('Missing recoverable file: ' + source);
    regularFile(backup, record.backup);
    if (!inside(destination, record.source)) throw Error('Unexpected destination');
    if (fs.statSync(record.backup).size !== record.bytes) throw Error('Backup size mismatch');
    return record;
  });
  const summary = {roots:names, assets:selected.length, files:files.length,
    logical_bytes:files.reduce((sum, file) => sum + file.bytes, 0),
    unique_bytes:[...new Map(files.map(file => [file.sha256, file.bytes])).values()].reduce((a,b)=>a+b,0),
    source_models:['assets/helicopter/heliport', 'assets/helicopter/helipad', 'assets/helicopter/mil_mi26tc'],
    backup, destination};
  const journal = path.join(workspace, 'artifacts/helicopter-model-restore-20261030.json');
  const apply = process.argv.includes('--apply');
  if (!apply && !process.argv.includes('--verify')) {console.log(JSON.stringify(summary, null, 2)); return;}
  if (apply) closed();
  const state = {summary, state:'verifying', restored:[]};
  const save = () => fs.writeFileSync(journal, JSON.stringify(state, null, 2));
  if (apply) save();
  // Verify the full selected backup before changing any game file. Originals stay untouched.
  const verified = new Set();
  for (const file of files) {
    if (!verified.has(file.sha256)) {
      if (await sha(file.backup) !== file.sha256) throw Error('Backup checksum mismatch: ' + file.backup);
      verified.add(file.sha256);
    }
    if (fs.existsSync(file.source)) {
      regularFile(destination, file.source);
      if (await sha(file.source) !== file.sha256)
        throw Error('Refusing to overwrite a different existing asset: ' + file.source);
    }
  }
  if (!apply) {console.log(JSON.stringify({...summary, verified:true})); return;}
  closed(); state.state = 'restoring'; save();
  const copies = new Map();
  for (const file of files) {
    if (state.restored.length % 100 === 0) closed();
    fs.mkdirSync(path.dirname(file.source), {recursive:true});
    for (let parent = path.dirname(file.source); parent !== destination; parent = path.dirname(parent))
      if (fs.lstatSync(parent).isSymbolicLink()) throw Error('Linked destination directory');
    if (!fs.existsSync(file.source)) {
      if (copies.has(file.sha256)) fs.linkSync(copies.get(file.sha256), file.source);
      else fs.copyFileSync(file.backup, file.source, fs.constants.COPYFILE_EXCL);
    }
    if (!copies.has(file.sha256)) {
      if (await sha(file.source) !== file.sha256) throw Error('Restored checksum mismatch');
      copies.set(file.sha256, file.source);
    }
    state.restored.push({path:file.source, sha256:file.sha256});
    if (state.restored.length % 100 === 0) save();
  }
  closed();
  const policy = path.join(game, 'ModsData/CityWeaver/HelicopterModels.json');
  state.previous_policy = fs.existsSync(policy) ? fs.readFileSync(policy, 'utf8') : null;
  save();
  fs.writeFileSync(policy, JSON.stringify({imported_models_enabled:true}, null, 2));
  state.state = 'completed'; state.completed_at = new Date().toISOString(); save();
  console.log(JSON.stringify({state:state.state, assets:selected.length, files:files.length,
    unique_bytes:summary.unique_bytes, imported_models_enabled:true, journal}));
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) await main();
