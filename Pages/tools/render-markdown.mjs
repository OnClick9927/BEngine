// Marked 17.0.5 is vendored under its MIT license; no npm installation is needed.
import { marked } from './vendor/marked.mjs';
let input = '';
for await (const chunk of process.stdin) input += chunk;
const documents = JSON.parse(input);
process.stdout.write(JSON.stringify(documents.map(text => marked.parse(text, { gfm: true }))));
