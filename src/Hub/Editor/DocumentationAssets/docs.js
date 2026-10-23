import { marked } from './marked.mjs';

const $ = id => document.getElementById(id);
const root = new URL('./', location.href);
const contentRoot = new URL('content/', root);
const labels = {
  zh: {
    skip:'跳到正文', documentation:'文档', engineManual:'引擎手册', packages:'扩展包',
    engineVersion:'引擎版本', refresh:'刷新', compatiblePackages:'兼容的扩展包',
    searchPackages:'搜索扩展包', documentNavigation:'文档导航', allPackages:'← 所有兼容扩展包',
    pages:'文档目录', onThisPage:'本页内容', pagination:'翻页', localDocumentation:'本地文档 · 请保持 Hub 运行',
    loading:'正在读取本地文档…', loadingCatalog:'正在读取已安装的引擎和兼容扩展包…',
    catalogNote:'{0} 个兼容扩展包', packageSummary:'适用于 BEngine {0} · 按本地安装的包实时生成',
    openDocs:'打开文档 →', noDocs:'这个版本未附带文档', noPackages:'没有兼容的扩展包',
    noResults:'没有符合搜索条件的扩展包', packageVersion:'版本 {0}', engineVersionNote:'引擎版本 {0}',
    packageVersionNote:'包版本 {0} · 适用于 BEngine {1}', minEngine:'最低引擎版本 {0}',
    engineKind:'BENGINE ENGINE MANUAL', packageKind:'BENGINE PACKAGE MANUAL',
    notFound:'没有找到这份文档', removedPackage:'这个包已移除，或不再兼容当前引擎版本。请重新选择扩展包。',
    missingDocument:'当前安装的版本没有这份文档。请从目录选择已有的文档。',
    catalogError:'无法读取本地文档目录，请确认 Hub 正在运行，然后重试。',
    documentError:'文档暂时无法加载，请刷新或从目录选择其他文档。', retry:'重新加载',
    previous:'← 上一篇', next:'下一篇 →', lightTheme:'切换为浅色', darkTheme:'切换为深色',
    engineTitle:'BEngine 引擎手册', packageTitle:'{0} 文档', homePage:'概览',
  },
  en: {
    skip:'Skip to content', documentation:'DOCUMENTATION', engineManual:'Engine manual', packages:'Packages',
    engineVersion:'Engine version', refresh:'Refresh', compatiblePackages:'Compatible packages',
    searchPackages:'Search packages', documentNavigation:'Documentation navigation', allPackages:'← All compatible packages',
    pages:'Documentation', onThisPage:'On this page', pagination:'Pagination', localDocumentation:'Local documentation · Keep Hub open',
    loading:'Loading local documentation…', loadingCatalog:'Reading installed engines and compatible packages…',
    catalogNote:'{0} compatible packages', packageSummary:'For BEngine {0} · Generated from your installed packages',
    openDocs:'Open documentation →', noDocs:'No documentation included in this version', noPackages:'No compatible packages installed',
    noResults:'No packages match your search', packageVersion:'Version {0}', engineVersionNote:'Engine version {0}',
    packageVersionNote:'Package version {0} · For BEngine {1}', minEngine:'Requires engine {0} or later',
    engineKind:'BENGINE ENGINE MANUAL', packageKind:'BENGINE PACKAGE MANUAL',
    notFound:'Documentation not found', removedPackage:'This package was removed or is no longer compatible with the selected engine. Choose another package.',
    missingDocument:'This document is not included in the installed version. Choose an available document from the navigation.',
    catalogError:'The local documentation catalog is unavailable. Make sure Hub is running, then try again.',
    documentError:'This document could not be loaded. Refresh or choose another document from the navigation.', retry:'Reload',
    previous:'← Previous', next:'Next →', lightTheme:'Switch to light theme', darkTheme:'Switch to dark theme',
    engineTitle:'BEngine engine manual', packageTitle:'{0} documentation', homePage:'Overview',
  },
};
const query = new URLSearchParams(location.search);
const storage = {
  get(key) { try { return localStorage.getItem(key); } catch { return null; } },
  set(key, value) { try { localStorage.setItem(key, value); } catch { /* Private browsers may disable storage. */ } },
};
const normalizeLanguage = value => value?.toLowerCase().startsWith('en') ? 'en' : 'zh';
let language = normalizeLanguage(storage.get('bengine-language') || query.get('lang') || navigator.language);
const storedTheme = storage.get('bengine-theme');
let theme = ['light','dark'].includes(storedTheme) ? storedTheme : ['light','dark'].includes(query.get('theme')) ? query.get('theme') : matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
let catalog;
let activeProduct;
let activeDocumentUrl;
let catalogRequest;
let documentRequest;
let loadRevision = 0;
let currentDocumentTitle = '';
let currentSections = [];
let lastCatalogFetch = 0;
let autoRefreshPending = false;

function t(key, ...values) {
  return (labels[language][key] || key).replace(/\{(\d+)\}/g, (_, index) => values[Number(index)] ?? '');
}
function create(tag, className, text) {
  const element = document.createElement(tag);
  if (className) element.className = className;
  if (text !== undefined) element.textContent = text;
  return element;
}
function originalName(tag, className, text) {
  const element = create(tag, className, text);
  element.translate = false;
  return element;
}
function route() {
  const params = new URLSearchParams(location.search);
  return {engine:params.get('engine') || catalog?.engine?.version || '', package:params.get('package') || '', page:params.get('page') || '', tab:params.get('tab') === 'packages' ? 'packages' : 'engine'};
}
function portalUrl(next = {}, hash = '') {
  const target = {...route(), ...next};
  const url = new URL(root);
  if (target.engine) url.searchParams.set('engine', target.engine);
  if (target.package) url.searchParams.set('package', target.package);
  if (target.page) url.searchParams.set('page', target.page);
  if (target.tab === 'packages') url.searchParams.set('tab', 'packages');
  url.searchParams.set('lang', language === 'en' ? 'en' : 'zh-CN');
  url.searchParams.set('theme', theme);
  url.hash = hash;
  return url;
}
function navigate(next, hash = '', replace = false) {
  const url = portalUrl(next, hash);
  if (replace) history.replaceState(null, '', url);
  else history.pushState(null, '', url);
  loadView(false);
}
function applyPreferences() {
  document.documentElement.lang = language === 'en' ? 'en' : 'zh-CN';
  document.documentElement.dataset.lang = language;
  document.documentElement.dataset.theme = theme;
  document.querySelectorAll('[data-i18n]').forEach(element => { element.textContent = t(element.dataset.i18n); });
  document.querySelectorAll('[data-i18n-label]').forEach(element => { element.setAttribute('aria-label', t(element.dataset.i18nLabel)); });
  document.querySelectorAll('[data-i18n-placeholder]').forEach(element => { element.placeholder = t(element.dataset.i18nPlaceholder); });
  $('language-toggle').textContent = language === 'en' ? '中文' : 'EN';
  $('language-toggle').setAttribute('aria-label', language === 'en' ? '切换为中文' : 'Switch to English');
  const themeLabel = t(theme === 'dark' ? 'lightTheme' : 'darkTheme');
  $('theme-toggle').setAttribute('aria-label', themeLabel);
  $('theme-toggle').title = themeLabel;
  $('theme-toggle').setAttribute('aria-pressed', String(theme === 'dark'));
  if (catalog) renderCatalogControls();
  if (activeProduct) renderProductControls();
  if (currentSections.length) renderSections();
}
function showStatus(message = '', error = false) {
  $('status').textContent = message;
  $('status').classList.toggle('error', error);
  $('status').hidden = !message;
}
function setDocumentBusy(busy) {
  $('document').setAttribute('aria-busy', String(busy));
}
function contentUrl(value, base = contentRoot) {
  if (typeof value !== 'string' || /[\\\u0000-\u001f\u007f]/.test(value)) return null;
  let url;
  try { url = new URL(value, base); } catch { return null; }
  if (url.origin !== root.origin || !url.pathname.startsWith(contentRoot.pathname) || url.username || url.password) return null;
  return url;
}
function productBase(product) { return contentUrl(product?.contentBaseUrl); }
function relativePage(url, product) {
  const base = productBase(product);
  return base && url.pathname.startsWith(base.pathname) ? url.pathname.slice(base.pathname.length) + url.search : '';
}
function documentUrlFor(product, page) {
  const base = productBase(product);
  if (!base) return null;
  const url = page ? contentUrl(page, base) : contentUrl(product.documentationUrl);
  return url && url.pathname.startsWith(base.pathname) ? url : null;
}
function documentPages(product) {
  const pages = Array.isArray(product?.pages) ? product.pages : [];
  return pages.filter(page => typeof page.title === 'string' && contentUrl(page.url) && relativePage(contentUrl(page.url), product)).map(page => ({...page, url:contentUrl(page.url)}));
}
function displayPageTitle(page) {
  return /^(index|readme)$/i.test(page.title) ? t('homePage') : page.title;
}
function renderCatalogControls() {
  const current = route();
  const select = $('engine-version');
  select.translate = false;
  select.replaceChildren();
  for (const entry of catalog.engines || []) {
    const option = originalName('option', '', entry.name || `BEngine ${entry.version}`);
    option.value = entry.version;
    select.append(option);
  }
  select.value = current.engine || catalog.engine.version;
  select.disabled = !select.options.length;
  $('catalog-note').textContent = t('catalogNote', catalog.packages.length);
  $('packages-summary').textContent = t('packageSummary', catalog.engine.version);
  const engineHref = portalUrl({package:'', page:'', tab:'engine'});
  const packagesHref = portalUrl({package:'', page:'', tab:'packages'});
  $('engine-tab').href = engineHref;
  $('brand-link').href = engineHref;
  $('packages-tab').href = packagesHref;
  $('back-packages').href = packagesHref;
  const packageMode = current.tab === 'packages' || !!current.package;
  $('engine-tab').toggleAttribute('aria-current', !packageMode);
  $('packages-tab').toggleAttribute('aria-current', packageMode);
  if (!packageMode) $('engine-tab').setAttribute('aria-current', 'page');
  else $('packages-tab').setAttribute('aria-current', 'page');
  renderPackages();
}
function renderPackages() {
  if (!catalog) return;
  const filter = $('package-search').value.trim().toLocaleLowerCase();
  const matches = catalog.packages.filter(product => `${product.name} ${product.id} ${product.description || ''}`.toLocaleLowerCase().includes(filter));
  const grid = $('package-grid');
  grid.replaceChildren();
  if (!matches.length) {
    grid.append(create('p', 'empty-state', t(filter ? 'noResults' : 'noPackages')));
    return;
  }
  for (const product of matches) {
    const card = create('article', 'package-card');
    card.append(originalName('h2', '', product.name), create('span', 'version', t('packageVersion', product.version)), create('p', '', product.description || ''));
    if (documentUrlFor(product, '')) {
      const link = create('a', 'button button-primary', t('openDocs'));
      link.href = portalUrl({package:product.id, page:'', tab:'engine'});
      card.append(link);
    } else card.append(create('span', 'no-docs', t('noDocs')));
    grid.append(card);
  }
}
function renderProductControls() {
  if (!activeProduct) return;
  const engine = activeProduct.id === 'engine';
  const kind = t(engine ? 'engineKind' : 'packageKind');
  const productTitle = engine ? t('engineTitle') : t('packageTitle', activeProduct.name);
  $('product-kind').textContent = kind;
  $('document-kind').textContent = kind;
  $('product-name').textContent = engine ? 'BEngine' : activeProduct.name;
  $('product-name').translate = false;
  $('product-version').textContent = t('packageVersion', activeProduct.version);
  $('document-title').textContent = currentDocumentTitle || productTitle;
  $('document-version').textContent = engine ? t('engineVersionNote', activeProduct.version) : t('packageVersionNote', activeProduct.version, catalog.engine.version);
  $('back-packages').hidden = engine;
  const pages = documentPages(activeProduct);
  const nav = $('page-nav');
  nav.replaceChildren();
  const currentPath = activeDocumentUrl?.pathname;
  for (const page of pages) {
    const link = create('a', '', displayPageTitle(page));
    link.href = portalUrl({package:engine ? '' : activeProduct.id, page:relativePage(page.url, activeProduct), tab:'engine'});
    if (page.url.pathname === currentPath) link.setAttribute('aria-current', 'page');
    nav.append(link);
  }
  nav.closest('details').hidden = !pages.length;
  const pagination = $('pagination');
  pagination.replaceChildren();
  const currentIndex = pages.findIndex(page => page.url.pathname === currentPath);
  if (currentIndex >= 0) {
    for (const offset of [-1, 1]) {
      const page = pages[currentIndex + offset];
      if (!page) continue;
      const link = create('a');
      link.href = portalUrl({package:engine ? '' : activeProduct.id, page:relativePage(page.url, activeProduct), tab:'engine'});
      link.append(create('small', '', t(offset < 0 ? 'previous' : 'next')), document.createTextNode(displayPageTitle(page)));
      pagination.append(link);
    }
  }
  pagination.hidden = !pagination.childElementCount;
  document.title = `${currentDocumentTitle || productTitle} · ${activeProduct.version} · BEngine`;
}
function readerError(message) {
  currentDocumentTitle = t('notFound');
  currentSections = [];
  $('section-nav').replaceChildren();
  $('section-nav').closest('details').hidden = true;
  const box = create('div', 'reader-error');
  box.append(create('p', '', message));
  const retry = create('button', 'button button-outline', t('retry'));
  retry.type = 'button';
  retry.addEventListener('click', () => loadView(true));
  box.append(retry);
  $('document-content').replaceChildren(box);
  if (activeProduct) renderProductControls();
  else {
    $('document-title').textContent = t('notFound');
    $('document-kind').textContent = '';
    $('document-version').textContent = '';
    $('product-name').textContent = '';
    $('product-kind').textContent = '';
    $('product-version').textContent = '';
    $('back-packages').hidden = false;
    $('page-nav').replaceChildren();
    $('page-nav').closest('details').hidden = true;
    $('pagination').replaceChildren();
  }
  setDocumentBusy(false);
}

// Documents are read as data. Their scripts, styles and resource origins never enter the shell.
const allowedTags = new Set('a abbr article b blockquote br caption code col colgroup dd del details div dl dt em figcaption figure h1 h2 h3 h4 h5 h6 hr i img kbd li main mark nav ol p picture pre s samp section small source span strong sub summary sup table tbody td th thead tfoot tr u ul var video audio'.split(' '));
const removeEntirely = new Set('script style iframe object embed form input button select textarea link meta base svg math canvas template noscript'.split(' '));
const allowedAttributes = new Set('id class title alt width height colspan rowspan scope open controls loop muted preload type start reversed value translate lang'.split(' '));
const originalTextTags = new Set('code pre kbd samp var'.split(' '));
function prepareDocument(raw, docUrl, markdown) {
  const parsed = document.createElement('template');
  parsed.innerHTML = markdown ? marked.parse(raw, {gfm:true}) : raw;
  const source = parsed.content.querySelector('main') || parsed.content.querySelector('article') || parsed.content;
  const fragment = document.createDocumentFragment();
  const idMap = new Map();
  let idNumber = 0;
  const sourceTitle = parsed.content.querySelector('main h1,article h1,h1')?.textContent?.trim() || '';
  function copyNode(node) {
    if (node.nodeType === Node.TEXT_NODE) return document.createTextNode(node.textContent);
    if (node.nodeType !== Node.ELEMENT_NODE) return null;
    const tag = node.localName.toLowerCase();
    if (removeEntirely.has(tag)) return null;
    if (!allowedTags.has(tag)) {
      const group = document.createDocumentFragment();
      for (const child of node.childNodes) { const clean = copyNode(child); if (clean) group.append(clean); }
      return group;
    }
    const element = document.createElement(tag === 'h1' ? 'h2' : tag);
    if (originalTextTags.has(tag)) element.translate = false;
    for (const attribute of node.attributes) {
      const key = attribute.name.toLowerCase();
      if (!allowedAttributes.has(key)) continue;
      if (key === 'id') {
        // Namespacing avoids document headings replacing controls in the reader shell.
        const cleanId = `doc-${++idNumber}`;
        if (!idMap.has(attribute.value)) idMap.set(attribute.value, cleanId);
        element.id = cleanId;
      } else if (key === 'class') {
        element.className = attribute.value.split(/\s+/).filter(token => ['grid','card','note','warning','table-wrap','lead','steps','pill','eyebrow'].includes(token)).join(' ');
      } else if (key === 'translate') {
        if (attribute.value.trim().toLowerCase() === 'no') element.translate = false;
      } else if (key === 'lang') {
        try {
          const languageTag = Intl.getCanonicalLocales(attribute.value.trim())[0];
          if (languageTag) element.lang = languageTag;
        } catch { /* Invalid language tags are omitted. */ }
      } else element.setAttribute(key, attribute.value);
    }
    if (/^h[1-6]$/.test(tag) && !element.id) {
      const originalId = node.textContent.trim().toLowerCase().replace(/[^\p{L}\p{N}\s_-]/gu, '').replace(/\s+/g, '-');
      element.id = `doc-${++idNumber}`;
      if (originalId && !idMap.has(originalId)) idMap.set(originalId, element.id);
    }
    if (tag === 'a') {
      const href = node.getAttribute('href');
      if (href) {
        try {
          const url = new URL(href, docUrl);
          const local = contentUrl(url.href);
          if (local) element.dataset.documentHref = local.href;
          else if (url.origin !== root.origin && ['http:','https:'].includes(url.protocol) && !url.username && !url.password) {
            element.href = url.href;
            element.target = '_blank';
            element.rel = 'noopener noreferrer';
          }
        } catch { /* Invalid destinations are presented as text. */ }
      }
    }
    if (['img','source','video','audio'].includes(tag)) {
      const sourceUrl = contentUrl(node.getAttribute('src'), docUrl);
      if (sourceUrl) element.src = sourceUrl.href;
      if (tag === 'img') { element.loading = 'lazy'; element.decoding = 'async'; }
      if (tag === 'video') {
        const poster = contentUrl(node.getAttribute('poster'), docUrl);
        if (poster) element.poster = poster.href;
      }
      if (tag === 'video' || tag === 'audio') { element.controls = true; element.preload = 'metadata'; }
    }
    for (const child of node.childNodes) { const clean = copyNode(child); if (clean) element.append(clean); }
    return element;
  }
  for (const node of source.childNodes) { const clean = copyNode(node); if (clean) fragment.append(clean); }
  fragment.querySelectorAll('a[data-document-href]').forEach(link => {
    const url = new URL(link.dataset.documentHref);
    delete link.dataset.documentHref;
    if (url.pathname === docUrl.pathname && url.search === docUrl.search && url.hash) {
      let original;
      try { original = decodeURIComponent(url.hash.slice(1)); } catch { original = ''; }
      const target = idMap.get(original);
      if (target) link.href = portalUrl({}, target);
    } else {
      const product = [catalog.engine, ...catalog.packages].find(entry => {
        const base = productBase(entry);
        return base && url.pathname.startsWith(base.pathname);
      });
      if (product && /\.(?:html?|md)$/i.test(url.pathname)) {
        // Keep the original fragment until the destination document assigns safe IDs.
        link.href = portalUrl({package:product.id === 'engine' ? '' : product.id, page:relativePage(url, product), tab:'engine'}, url.hash);
      } else if (product && !/\.(?:html?|md)$/i.test(url.pathname)) {
        link.href = url.href;
        link.target = '_blank';
        link.rel = 'noopener noreferrer';
      }
    }
  });
  fragment.querySelectorAll('table').forEach(table => {
    if (table.parentElement?.classList.contains('table-wrap')) return;
    const wrapper = create('div', 'table-wrap');
    table.replaceWith(wrapper);
    wrapper.append(table);
  });
  return {fragment, title:sourceTitle, idMap};
}
function renderSections() {
  const nav = $('section-nav');
  nav.replaceChildren();
  for (const section of currentSections) {
    const link = create('a', section.level === 'H3' ? 'level-3' : '', section.title);
    link.href = portalUrl({}, section.id);
    nav.append(link);
  }
  nav.closest('details').hidden = !currentSections.length;
}
function finishDocument(idMap) {
  const existingIds = new Set([...document.querySelectorAll('[id]')].map(element => element.id));
  currentSections = [...$('document-content').querySelectorAll('h2,h3')].map((heading, index) => {
    if (!heading.id) {
      let id = `doc-heading-${index + 1}`;
      while (existingIds.has(id)) id += '-heading';
      heading.id = id;
      existingIds.add(id);
    }
    return {id:heading.id, title:heading.textContent, level:heading.tagName};
  });
  renderProductControls();
  renderSections();
  setDocumentBusy(false);
  if (location.hash) {
    let hash;
    try { hash = decodeURIComponent(location.hash.slice(1)); } catch { hash = ''; }
    const safeId = idMap.get(hash) || hash;
    const target = $('document-content').querySelector(`#${CSS.escape(safeId)}`);
    if (target) {
      if (safeId !== hash) history.replaceState(null, '', portalUrl({}, safeId));
      target.scrollIntoView({block:'start', behavior:'instant'});
    }
  }
}
async function fetchCatalog(engine) {
  catalogRequest?.abort();
  const controller = new AbortController();
  catalogRequest = controller;
  const url = new URL('api/catalog', root);
  if (engine) url.searchParams.set('engine', engine);
  const response = await fetch(url, {cache:'no-store', signal:controller.signal});
  if (!response.ok) throw new Error('catalog');
  const next = await response.json();
  if (!next.engine || typeof next.engine.version !== 'string' || !Array.isArray(next.engines) || !Array.isArray(next.packages)) throw new Error('catalog');
  catalog = next;
  lastCatalogFetch = Date.now();
  return catalog;
}
async function loadView(refresh = false) {
  const revision = ++loadRevision;
  documentRequest?.abort();
  const controller = new AbortController();
  documentRequest = controller;
  const requested = route();
  $('refresh').disabled = true;
  let readingCatalog = false;
  try {
    if (refresh || !catalog || catalog.engine.version !== requested.engine) {
      readingCatalog = true;
      showStatus(t('loadingCatalog'));
      await fetchCatalog(requested.engine);
      if (revision !== loadRevision) return;
      readingCatalog = false;
    }
    if (!requested.engine) history.replaceState(null, '', portalUrl({engine:catalog.engine.version}));
    renderCatalogControls();
    showStatus();
    const packagesView = requested.tab === 'packages' && !requested.package;
    $('package-browser').hidden = !packagesView;
    $('reader').hidden = packagesView;
    if (packagesView) {
      activeProduct = null;
      document.title = `${t('compatiblePackages')} · BEngine ${catalog.engine.version}`;
      return;
    }
    activeProduct = requested.package ? catalog.packages.find(entry => entry.id === requested.package) : catalog.engine;
    activeDocumentUrl = null;
    currentDocumentTitle = '';
    currentSections = [];
    if (!activeProduct) { readerError(t('removedPackage')); return; }
    const docUrl = documentUrlFor(activeProduct, requested.page);
    renderProductControls();
    if (!docUrl) { readerError(t(activeProduct.documentationUrl ? 'missingDocument' : 'noDocs')); return; }
    activeDocumentUrl = docUrl;
    setDocumentBusy(true);
    $('document-content').replaceChildren(create('p', 'loading', t('loading')));
    $('section-nav').replaceChildren();
    $('pagination').replaceChildren();
    renderProductControls();
    const response = await fetch(docUrl, {cache:'no-store', signal:controller.signal});
    if (revision !== loadRevision) return;
    if (!response.ok) throw new Error('document');
    const raw = await response.text();
    if (revision !== loadRevision) return;
    const prepared = prepareDocument(raw, docUrl, /\.md$/i.test(docUrl.pathname));
    $('document-content').replaceChildren(prepared.fragment);
    currentDocumentTitle = prepared.title;
    finishDocument(prepared.idMap);
  } catch (error) {
    if (revision !== loadRevision || error.name === 'AbortError') return;
    if (readingCatalog) {
      if (!catalog || catalog.engine.version !== requested.engine) {
        $('reader').hidden = true;
        $('package-browser').hidden = true;
      }
      showStatus(t('catalogError'), true);
    } else readerError(t('documentError'));
  } finally {
    if (revision === loadRevision) $('refresh').disabled = false;
  }
}

async function refreshCatalogInBackground() {
  if (document.hidden || autoRefreshPending || $('refresh').disabled || Date.now() - lastCatalogFetch < 1000) return;
  autoRefreshPending = true;
  const revision = loadRevision;
  const requested = route();
  const previousProduct = activeProduct;
  try {
    await fetchCatalog(requested.engine);
    if (revision !== loadRevision) return;
    renderCatalogControls();
    if (previousProduct) {
      const nextProduct = requested.package ? catalog.packages.find(entry => entry.id === requested.package) : catalog.engine;
      if (!nextProduct || nextProduct.version !== previousProduct.version || nextProduct.documentationUrl !== previousProduct.documentationUrl || JSON.stringify(nextProduct.pages) !== JSON.stringify(previousProduct.pages)) await loadView(false);
      else { activeProduct = nextProduct; renderProductControls(); }
    }
  } catch (error) {
    if (revision === loadRevision && error.name !== 'AbortError') showStatus(t('catalogError'), true);
  } finally { autoRefreshPending = false; }
}

document.addEventListener('click', event => {
  const link = event.target.closest('a[href]');
  if (!link || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey || link.target === '_blank') return;
  const url = new URL(link.href);
  if (url.origin !== root.origin || url.pathname !== root.pathname) return;
  event.preventDefault();
  const current = new URL(location.href);
  if (url.search === current.search && url.hash) {
    history.pushState(null, '', url);
    let id;
    try { id = decodeURIComponent(url.hash.slice(1)); } catch { id = ''; }
    $(id)?.scrollIntoView({block:'start', behavior:'instant'});
    return;
  }
  history.pushState(null, '', url);
  window.scrollTo({top:0, behavior:'instant'});
  loadView(false);
});
$('engine-version').addEventListener('change', () => { navigate({engine:$('engine-version').value, package:'', page:'', tab:route().tab}); });
$('package-search').addEventListener('input', renderPackages);
$('refresh').addEventListener('click', () => loadView(true));
$('language-toggle').addEventListener('click', () => {
  language = language === 'zh' ? 'en' : 'zh';
  storage.set('bengine-language', language);
  history.replaceState(null, '', portalUrl({}, location.hash));
  applyPreferences();
});
$('theme-toggle').addEventListener('click', () => {
  theme = theme === 'dark' ? 'light' : 'dark';
  storage.set('bengine-theme', theme);
  history.replaceState(null, '', portalUrl({}, location.hash));
  applyPreferences();
});
window.addEventListener('popstate', () => loadView(false));
window.addEventListener('focus', refreshCatalogInBackground);
document.addEventListener('visibilitychange', refreshCatalogInBackground);
applyPreferences();
loadView(true);
