import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
const [platformPath, output] = process.argv.slice(2);
const source = fs.readFileSync(platformPath, 'utf8').replace(/^import .*;\r?$/gm, '');
const { decodeHostLaunchEnvironment: decode } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const cases = JSON.parse(fs.readFileSync(path.join(output, 'WireCases.json'), 'utf8'));
let checks = 0;
for (const test of cases) {
  if (!test.Valid) assert.throws(() => decode(test.Encoded));
  else {
    const decoded = decode(test.Encoded, '/private');
    const expected = { ...test.Environment };
    for (const [key, value] of Object.entries(test.RelativeFiles || {})) expected[key] = '/private/' + value;
    assert.deepEqual({ ...decoded }, expected);
    assert.equal(Object.getPrototypeOf(decoded), null);
  }
  checks++;
}
assert.deepEqual({ ...decode(null) }, {}); checks++;
assert.equal(Object.prototype.polluted, undefined); checks++;
fs.writeFileSync(path.join(output, 'WebResult.json'), JSON.stringify({ Passed: true, Checks: checks, SharedWireCases: cases.length }, null, 2));
console.log('WEB_LAUNCH_BOUNDARY_OK|checks=' + checks);
