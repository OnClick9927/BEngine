#!/usr/bin/env python3
"""Export versioned offline manuals into safe, website-local HTML fragments.

Run from any directory: python Pages/tools/export-docs.py
Requires Python 3.10+ and Node.js 18+ (the Markdown renderer is vendored).
Published Engine/BEgine and Engine/Packages folders are the version authority.
The current engine's platform manual is supplemented from src/Platform.
Existing historical versions are retained unchanged; the newest published version
is refreshed. --refresh-history explicitly regenerates older published snapshots.
No validation files or temporary files are written into the website.
"""
from __future__ import annotations

import argparse
import html
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import shutil
import subprocess
from urllib.parse import quote, unquote, urlencode, urlsplit

PAGES = Path(__file__).resolve().parents[1]
REPO = PAGES.parent
MANIFEST = PAGES / 'data/documentation.json'
DOC_EXT = {'.md', '.markdown', '.html', '.htm'}
IMAGE_EXT = {'.png', '.jpg', '.jpeg', '.gif', '.webp', '.svg', '.avif', '.bmp'}
SEMVER = re.compile(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?')
PRODUCT_ID = re.compile(r'[a-zA-Z0-9]+(?:[.-][a-zA-Z0-9]+)*')
PAGE_ID = re.compile(r'[a-zA-Z0-9_]+(?:[-.][a-zA-Z0-9_]+)*')


def key(path: Path) -> str:
    return str(path.resolve()).casefold()


def semver(value: str):
    match = SEMVER.fullmatch(value)
    if not match:
        raise ValueError(f'Invalid semantic version: {value!r}')
    prerelease = match.group(4)
    identifiers = []
    if prerelease:
        for token in prerelease.split('.'):
            if token.isdigit():
                if len(token) > 1 and token.startswith('0'):
                    raise ValueError(f'Invalid numeric prerelease identifier: {value!r}')
                identifiers.append((0, int(token)))
            else:
                identifiers.append((1, token))
    # Numeric prerelease identifiers compare numerically and sort before text;
    # a release sorts after its prereleases. Build metadata has no precedence.
    return (*map(int, match.group(1, 2, 3)), prerelease is None, tuple(identifiers))


def documentation_path(relative: str) -> Path:
    output = (PAGES / relative).resolve()
    destination_root = (PAGES / 'documentation').resolve()
    if output == destination_root or not output.is_relative_to(destination_root):
        raise ValueError(f'Documentation output escapes its directory: {relative!r}')
    return output


def validate_identity(product_id: str, version: str):
    if not PRODUCT_ID.fullmatch(product_id):
        raise ValueError(f'Invalid documentation product ID: {product_id!r}')
    semver(version)


def field(text: str, name: str, default: str = '') -> str:
    match = re.search(rf'^{re.escape(name)}:\s*(.*?)\s*$', text, re.M)
    return match.group(1).strip('\'"') if match else default


def slug(text: str) -> str:
    plain = html.unescape(re.sub(r'<[^>]+>', '', text)).strip().lower()
    plain = re.sub(r'[^\w\s\-\u3400-\u9fff]', '', plain)
    return re.sub(r'\s+', '-', plain) or 'section'


def first_title(text: str, fallback: str) -> str:
    match = re.search(r'^#\s+(.+)$', text, re.M)
    if not match:
        match = re.search(r'<h1[^>]*>(.*?)</h1>', text, re.I | re.S)
    if not match:
        match = re.search(r'<title[^>]*>(.*?)</title>', text, re.I | re.S)
    return html.unescape(re.sub(r'<[^>]+>', '', match.group(1))).strip() if match else fallback


def route(product: str, version: str, doc: str = 'overview', anchor: str = '') -> str:
    result = 'manual.html?' + urlencode({'product': product, 'version': version, 'doc': doc})
    return result + ('#' + quote(unquote(anchor), safe='-_.~') if anchor else '')


class Exporter:
    def __init__(self, refresh_history: bool):
        self.old = json.loads(MANIFEST.read_text(encoding='utf-8')) if MANIFEST.exists() else {'products': []}
        for product in self.old['products']:
            for version in product['versions']:
                validate_identity(product['id'], version['version'])
                for page in version['pages']:
                    if not PAGE_ID.fullmatch(page['id']):
                        raise ValueError(f'Invalid documentation page ID: {page["id"]!r}')
                    expected = f'documentation/{product["id"]}/{version["version"]}/{page["id"]}.html'
                    if page['path'] != expected:
                        raise ValueError(f'Unexpected documentation page path: {page["path"]!r}')
                    documentation_path(page['path'])
        self.products = {p['id']: p for p in self.old['products']}
        self.targets: dict[str, tuple[str, str, str]] = {}
        self.versions: list[dict] = []
        self.pages: list[dict] = []
        self.refresh_history = refresh_history
        self.fallbacks: dict[str, str] = {}
        self.images: set[str] = set()

    def add_version(self, product_id: str, name: str, version: str, root: Path,
                    aliases: list[Path], kind='package', minimum='', supplement=None):
        validate_identity(product_id, version)
        current = self.products.setdefault(product_id, {
            'id': product_id, 'name': name, 'kind': kind, 'defaultVersion': version, 'versions': []})
        current['name'] = name
        current['kind'] = kind
        current['defaultVersion'] = max([version] + [v['version'] for v in current['versions']], key=semver)
        previous = next((v for v in current['versions'] if v['version'] == version), None)
        record = {'version': version, 'pages': []}
        if minimum:
            record['minimumEngineVersion'] = minimum
        preserve_snapshot = previous and version != current['defaultVersion'] and not self.refresh_history
        files = []
        overview = root / 'Editor/Readme.md'
        if overview.exists():
            files.append((overview, 'overview'))
        for candidate in sorted((root / 'Editor/Doc').rglob('*')):
            if candidate.is_file() and candidate.suffix.lower() in DOC_EXT:
                relative = candidate.relative_to(root / 'Editor/Doc')
                stem = relative.stem.lower()
                if stem == 'index':
                    stem = 'reference'
                files.append((candidate, stem))
        if supplement:
            # Platform docs describe the same current source release. Include their
            # local Markdown references so no source-directory links escape online.
            for candidate in sorted(supplement.glob('*.md')):
                files.append((candidate, 'platform' if candidate.name.lower() == 'readme.md' else candidate.stem.lower()))
        used = set()
        for source, doc_id in files:
            if doc_id in used:
                relative = source.relative_to(root) if source.is_relative_to(root) else Path(source.name)
                doc_id = '-'.join(p.lower() for p in relative.with_suffix('').parts if p.lower() not in {'editor', 'doc'})
                if doc_id.endswith('-index'):
                    doc_id = doc_id[:-6] + '-reference'
            original = doc_id
            number = 2
            while doc_id in used:
                doc_id = f'{original}-{number}'
                number += 1
            used.add(doc_id)
            if not PAGE_ID.fullmatch(doc_id):
                raise ValueError(f'Invalid documentation page ID derived from {source}: {doc_id!r}')
            if preserve_snapshot and not any(p['id'] == doc_id for p in previous['pages']):
                continue
            identity = (product_id, version, doc_id)
            self.targets[key(source)] = identity
            if source.is_relative_to(root):
                relative = source.relative_to(root)
                for alias in aliases:
                    self.targets.setdefault(key(alias / relative), identity)
            if preserve_snapshot:
                continue
            text = source.read_text(encoding='utf-8-sig')
            destination = f'documentation/{product_id}/{version}/{doc_id}.html'
            documentation_path(destination)
            page = {'id': doc_id, 'title': first_title(text, source.stem), 'path': destination}
            record['pages'].append(page)
            work = {'source': source, 'text': text, 'page': page, 'product': product_id,
                    'version': version, 'root': root, 'aliases': aliases}
            self.pages.append(work)
        if preserve_snapshot:
            return
        if not record['pages']:
            raise ValueError(f'No documentation for {product_id} {version}')
        if previous:
            # A source checkout may have advanced beyond the last exported
            # release. Retain supplemental pages already captured for that
            # release instead of silently removing its published manual pages.
            for page in previous['pages']:
                if page['id'] not in used and documentation_path(page['path']).is_file():
                    record['pages'].append(page)
        current['versions'] = [v for v in current['versions'] if v['version'] != version] + [record]
        self.versions.append(record)

    def discover(self):
        engines = []
        for root in (REPO / 'Engine/BEgine').glob('BEgine-*'):
            if (root / 'Editor/Readme.md').exists():
                engines.append((root.name.removeprefix('BEgine-'), root))
        properties = (REPO / 'src/Directory.Build.props').read_text(encoding='utf-8-sig')
        source_version_match = re.search(r'<Version>\s*([^<]+?)\s*</Version>', properties)
        source_version = source_version_match.group(1) if source_version_match else ''
        for version, root in sorted(engines, key=lambda item: semver(item[0]), reverse=True):
            self.add_version('engine', 'BEngine 引擎', version, root, [REPO / 'src/Core'],
                             kind='engine', supplement=REPO / 'src/Platform' if version == source_version else None)
        packages = []
        for manifest in (REPO / 'Engine/Packages').glob('*/*/package.yaml'):
            content = manifest.read_text(encoding='utf-8-sig')
            packages.append((field(content, 'id'), field(content, 'displayName'), field(content, 'packageVersion'),
                             manifest.parent, field(content, 'minimumEngineVersion')))
        # Newest first ensures aliases shared by source checkouts point to current docs.
        for product, name, version, root, minimum in sorted(packages, key=lambda p: semver(p[2]), reverse=True):
            if not product or not version:
                raise ValueError(f'Invalid package manifest: {root}')
            alias = REPO / 'src/Packages' / root.parent.name
            self.add_version(product, name, version, root, [alias], minimum=minimum)
        # The source and repository paths in old offline links can refer to either
        # versioned distribution or source aliases; index all preserved snapshots too.
        for product in self.products.values():
            product['versions'].sort(key=lambda v: semver(v['version']), reverse=True)

    def resolve_local(self, raw: str, work: dict) -> Path | None:
        raw = unquote(raw).replace('\\', '/')
        source, root = work['source'], work['root']
        candidates = [source.parent / raw, root / raw, root / 'Editor' / raw,
                      root / 'Editor/Doc' / raw, REPO / raw, REPO.parent / raw]
        # Some offline manuals were copied into an extra Doc/<Package> folder
        # without adjusting their original image links. Match the real file.
        if '/images/' in raw or raw.startswith('images/'):
            candidates += list((root / 'Editor/Doc').rglob(Path(raw).name))
        for candidate in candidates:
            if candidate.is_file() or key(candidate) in self.targets:
                return candidate.resolve()
        return None

    def website_fallback(self, raw: str) -> str:
        lower = raw.lower()
        if 'license' in lower or 'licence' in lower:
            return 'about.html#license'
        if 'example' in lower or 'sample' in lower:
            return 'examples.html'
        if 'package' in lower:
            return 'packages.html'
        return 'about.html#source'

    def rewrite_url(self, raw: str, work: dict, image=False) -> str | None:
        raw = html.unescape(raw).strip()
        if raw.startswith('#'):
            return raw
        parsed = urlsplit(raw)
        if parsed.scheme in {'http', 'https'}:
            if parsed.hostname and parsed.hostname.lower() in {'github.com', 'raw.githubusercontent.com'}:
                path = unquote(parsed.path)
                repository = path.strip('/').split('/')[:2]
                if [part.casefold() for part in repository] != ['onclick9927', 'bengine']:
                    # Third-party references must not be mistaken for this
                    # project's source. Keep their text and disclose the actual
                    # reference address without creating an off-site link.
                    work.setdefault('external_references', {})[raw] = raw
                    return None
                # Resolve repository-relative source links inside the website.
                marker = re.search(r'/(?:blob|tree)/(?:main|master)/(.+)', path)
                repository_path = marker.group(1) if marker else None
                if parsed.hostname.lower() == 'raw.githubusercontent.com':
                    raw_parts = path.strip('/').split('/', 3)
                    if len(raw_parts) == 4 and raw_parts[2] in {'main', 'master'}:
                        repository_path = raw_parts[3]
                if repository_path:
                    local = self.resolve_local(repository_path, work)
                    target = self.targets.get(key(local)) if local else None
                    if target:
                        return route(*target, parsed.fragment)
                replacement = self.website_fallback(path)
                self.fallbacks[raw] = replacement
                return replacement if not image else None
            return raw if not image else None
        if parsed.scheme in {'mailto', 'tel'} and not image:
            return raw
        if parsed.scheme or raw.startswith('//'):
            return None
        local = self.resolve_local(parsed.path, work)
        if local:
            target = self.targets.get(key(local))
            if target:
                return route(*target, parsed.fragment)
            if local.suffix.lower() in IMAGE_EXT:
                # Assets are versioned with their manual, so old versions keep
                # their screenshots when a new release is exported.
                if not local.is_relative_to(work['root']):
                    return None
                relative = local.relative_to(work['root']).as_posix()
                destination = f"documentation/{work['product']}/{work['version']}/assets/{relative}"
                out = documentation_path(destination)
                out.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(local, out)
                self.images.add(destination)
                return quote(destination, safe='/-_.')
        if image:
            raise ValueError(f"Image cannot be exported: {work['source']}: {raw}")
        if Path(parsed.path).suffix.lower() in DOC_EXT:
            # A few distributed manuals retain links to pages that were never
            # included in that release. Point to the same version's reference
            # manual and disclose this in the link title, rather than implying
            # that a source-information page is the missing runtime guide.
            version = next(v for v in self.products[work['product']]['versions'] if v['version'] == work['version'])
            doc = 'reference' if any(p['id'] == 'reference' for p in version['pages']) else 'overview'
            replacement = route(work['product'], work['version'], doc)
            work.setdefault('missing_links', set()).add(raw)
            self.fallbacks[raw] = replacement
            return replacement
        replacement = self.website_fallback(raw)
        self.fallbacks[raw] = replacement
        return replacement

    def export(self):
        markdown = [p for p in self.pages if p['source'].suffix.lower() in {'.md', '.markdown'}]
        node = shutil.which('node')
        if not node:
            raise RuntimeError('Node.js 18+ is required to render Markdown; no npm install is needed.')
        rendered = subprocess.run([node, str(PAGES / 'tools/render-markdown.mjs')],
                                  input=json.dumps([p['text'] for p in markdown], ensure_ascii=False),
                                  text=True, encoding='utf-8', capture_output=True, check=True)
        for work, rendered_html in zip(markdown, json.loads(rendered.stdout), strict=True):
            work['html'] = rendered_html
        for work in self.pages:
            parser = SafeFragment(self, work)
            parser.feed(work.get('html', work['text']))
            parser.close()
            output = documentation_path(work['page']['path'])
            output.parent.mkdir(parents=True, exist_ok=True)
            fragment = re.sub(r'(?m)[ \t]+$', '', ''.join(parser.output).strip())
            output.write_text(fragment + '\n', encoding='utf-8')
        result = {'schemaVersion': 1, 'products': sorted(self.products.values(), key=lambda p: (p['kind'] != 'engine', p['name'].casefold()))}
        MANIFEST.parent.mkdir(parents=True, exist_ok=True)
        MANIFEST.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print(json.dumps({'products': len(result['products']), 'versions': sum(len(p['versions']) for p in result['products']),
                          'renderedPages': len(self.pages), 'copiedImages': len(self.images),
                          'mappedLocalLinks': self.fallbacks}, ensure_ascii=False, indent=2))


class SafeFragment(HTMLParser):
    ALLOWED = {'a', 'p', 'br', 'hr', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'ul', 'ol', 'li',
               'pre', 'code', 'blockquote', 'strong', 'em', 'b', 'i', 's', 'del', 'table', 'thead',
               'tbody', 'tfoot', 'tr', 'th', 'td', 'img', 'figure', 'figcaption', 'kbd', 'samp',
               'details', 'summary', 'div', 'span', 'section', 'article', 'dl', 'dt', 'dd', 'sup', 'sub'}
    DROP = {'head', 'script', 'style', 'nav', 'footer', 'iframe', 'object', 'embed', 'form', 'button', 'input', 'textarea', 'select'}
    VOID = {'br', 'hr', 'img'}

    def __init__(self, exporter: Exporter, work: dict):
        super().__init__(convert_charrefs=True)
        self.exporter, self.work = exporter, work
        self.output: list[str] = []
        self.skipped: list[str] = []
        self.heading_start = None
        self.heading_tag = None
        self.heading_ids: set[str] = set()
        self.anchor_tags: list[str] = []

    def handle_starttag(self, tag, attrs):
        if self.skipped:
            if tag not in self.VOID and tag not in {'meta', 'link', 'input', 'embed'}:
                self.skipped.append(tag)
            return
        if tag in self.DROP:
            if tag not in {'input', 'embed'}:
                self.skipped.append(tag)
            return
        if tag not in self.ALLOWED:
            return
        attributes = dict(attrs)
        safe = []
        for name in ('id', 'title', 'alt', 'colspan', 'rowspan', 'start', 'open'):
            if name in attributes and attributes[name] is not None:
                safe.append((name, attributes[name]))
        if tag == 'a':
            original_url = attributes.get('href', '')
            destination = self.exporter.rewrite_url(original_url, self.work)
            if destination:
                safe.append(('href', destination))
            if original_url in self.work.get('missing_links', set()):
                safe = [(n, v) for n, v in safe if n != 'title']
                safe.append(('title', '此独立页面未随本版本发布，打开本版本详细手册'))
            external_reference = self.work.get('external_references', {}).get(original_url.strip())
            if external_reference:
                safe = [(n, v) for n, v in safe if n not in {'href', 'title'}]
                safe.append(('title', '外部参考：' + external_reference))
                tag = 'span'
            self.anchor_tags.append(tag)
        if tag == 'img':
            destination = self.exporter.rewrite_url(attributes.get('src', ''), self.work, image=True)
            if not destination:
                return
            safe += [('src', destination), ('loading', 'lazy'), ('decoding', 'async')]
        if tag == 'code' and re.fullmatch(r'language-[\w#+.-]+', attributes.get('class', '')):
            safe.append(('class', attributes['class']))
        if re.fullmatch(r'h[1-6]', tag) and not attributes.get('id'):
            self.heading_start = len(self.output)
            self.heading_tag = tag
        self.output.append('<' + tag + ''.join(f' {n}="{html.escape(v, quote=True)}"' for n, v in safe) + '>')

    def handle_endtag(self, tag):
        if self.skipped:
            if tag == self.skipped[-1]:
                self.skipped.pop()
            return
        if tag not in self.ALLOWED or tag in self.VOID:
            return
        if self.heading_tag == tag and self.heading_start is not None:
            title = ''.join(self.output[self.heading_start + 1:])
            identifier = slug(title)
            original, index = identifier, 1
            while identifier in self.heading_ids:
                identifier = f'{original}-{index}'
                index += 1
            self.heading_ids.add(identifier)
            self.output[self.heading_start] = f'<{tag} id="{html.escape(identifier, quote=True)}">'
            self.heading_start = self.heading_tag = None
        output_tag = self.anchor_tags.pop() if tag == 'a' and self.anchor_tags else tag
        self.output.append(f'</{output_tag}>')

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in self.VOID and tag not in {'meta', 'link', 'input', 'embed'}:
            self.handle_endtag(tag)

    def handle_data(self, data):
        if not self.skipped:
            self.output.append(html.escape(data))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--refresh-history', action='store_true', help='Explicitly refresh older published documentation versions too.')
    args = parser.parse_args()
    exporter = Exporter(args.refresh_history)
    exporter.discover()
    if not exporter.products:
        raise RuntimeError('No published engine or package versions found.')
    exporter.export()


if __name__ == '__main__':
    main()
