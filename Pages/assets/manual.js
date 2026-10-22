(() => {
  'use strict';
  const content = document.getElementById('manual-content');
  if (!content) return;
  const productSelect = document.getElementById('manual-product');
  const versionSelect = document.getElementById('manual-version');
  const pageNav = document.getElementById('manual-pages');
  const sectionNav = document.getElementById('manual-sections');
  const pagination = document.getElementById('manual-pagination');
  const params = new URLSearchParams(location.search);
  let catalog;
  let product;
  let version;
  let currentPage;

  const create = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
  };
  const manualUrl = (productId, versionId, docId = 'overview') => {
    const query = new URLSearchParams({ product: productId, version: versionId, doc: docId });
    return `manual.html?${query}`;
  };

  function showError(title, message, recovery) {
    content.replaceChildren();
    const box = create('div', 'manual-error');
    box.append(create('h2', '', title), create('p', '', message));
    const actions = create('div', 'button-row');
    if (recovery) {
      const back = create('a', 'button button-dark', recovery.text);
      back.href = recovery.href;
      actions.append(back);
    }
    const retry = create('button', 'button button-outline', '重新加载');
    retry.type = 'button';
    retry.addEventListener('click', () => location.reload());
    const docs = create('a', 'text-link', '返回文档中心 →');
    docs.href = 'docs.html';
    actions.append(retry, docs);
    box.append(actions);
    content.append(box);
    content.setAttribute('aria-busy', 'false');
  }

  function prepareFragment(html) {
    const documentFragment = document.createElement('template');
    documentFragment.innerHTML = html;
    documentFragment.content.querySelectorAll('script,style,iframe,object,embed,form,link,meta,base').forEach(element => element.remove());
    documentFragment.content.querySelectorAll('*').forEach(element => {
      for (const attribute of [...element.attributes]) {
        if (attribute.name.toLowerCase().startsWith('on') || attribute.name === 'srcdoc') element.removeAttribute(attribute.name);
        if (['href', 'src', 'xlink:href'].includes(attribute.name) && /^\s*(javascript|vbscript|data):/i.test(attribute.value)) element.removeAttribute(attribute.name);
      }
    });
    documentFragment.content.querySelectorAll('h1').forEach(heading => {
      const replacement = create('h2');
      for (const attribute of heading.attributes) replacement.setAttribute(attribute.name, attribute.value);
      replacement.append(...heading.childNodes);
      heading.replaceWith(replacement);
    });
    documentFragment.content.querySelectorAll('table').forEach(table => {
      if (table.parentElement?.classList.contains('table-wrap')) return;
      const wrap = create('div', 'manual-table-wrap');
      table.replaceWith(wrap);
      wrap.append(table);
    });
    documentFragment.content.querySelectorAll('img').forEach(image => {
      image.loading = 'lazy';
      image.decoding = 'async';
    });
    return documentFragment.content;
  }

  function buildSections() {
    sectionNav.replaceChildren();
    const existingIds = new Set([...document.querySelectorAll('[id]')].map(element => element.id));
    content.querySelectorAll('h2,h3').forEach((heading, index) => {
      if (!heading.id) {
        let id = `section-${index + 1}`;
        while (existingIds.has(id)) id += '-heading';
        heading.id = id;
        existingIds.add(id);
      }
      const link = create('a', '', heading.textContent);
      link.href = `#${encodeURIComponent(heading.id)}`;
      if (heading.tagName === 'H3') link.style.paddingLeft = '17px';
      sectionNav.append(link);
    });
    sectionNav.closest('details').hidden = !sectionNav.childElementCount;
  }

  function renderControls() {
    productSelect.replaceChildren();
    for (const entry of catalog.products) {
      const option = create('option', '', entry.kind === 'engine' ? 'BEngine 引擎' : entry.name);
      option.value = entry.id;
      productSelect.append(option);
    }
    productSelect.value = product.id;
    productSelect.disabled = false;
    versionSelect.replaceChildren();
    for (const entry of product.versions) {
      const option = create('option', '', entry.version);
      option.value = entry.version;
      versionSelect.append(option);
    }
    versionSelect.value = version?.version || product.defaultVersion;
    versionSelect.disabled = false;
    document.getElementById('manual-product-label').textContent = product.kind === 'engine' ? 'BEngine 引擎' : product.name;
    document.getElementById('manual-kind').textContent = product.kind === 'engine' ? 'BENGINE ENGINE MANUAL' : 'BENGINE PACKAGE MANUAL';
    document.getElementById('manual-title').textContent = product.kind === 'engine' ? 'BEngine 引擎手册' : `${product.name} 文档`;
  }

  async function load() {
    try {
      const response = await fetch('data/documentation.json?v=versioned-docs-1');
      if (!response.ok) throw new Error('Documentation catalog could not be loaded');
      catalog = await response.json();
      if (catalog.schemaVersion !== 1 || !Array.isArray(catalog.products) || !catalog.products.length) throw new Error('Invalid documentation catalog');
      const productId = params.get('product') || 'engine';
      product = catalog.products.find(entry => entry.id === productId);
      if (!product) {
        showError('没有找到这份文档', '请从引擎文档或扩展包目录选择需要阅读的手册。', { text: '浏览扩展包', href: 'packages.html' });
        return;
      }
      const versionId = params.get('version') || product.defaultVersion;
      version = product.versions.find(entry => entry.version === versionId);
      renderControls();
      if (!version) {
        versionSelect.selectedIndex = -1;
        document.getElementById('manual-version-note').textContent = `请求的版本：${versionId}`;
        showError('这个版本的文档尚未收录', `当前可阅读版本：${product.versions.map(entry => entry.version).join('、')}。请选择已有版本。`, { text: `阅读 ${product.defaultVersion} 文档`, href: manualUrl(product.id, product.defaultVersion) });
        return;
      }
      const pageId = params.get('doc') || 'overview';
      currentPage = version.pages.find(entry => entry.id === pageId);
      document.getElementById('manual-version-note').textContent = `${product.kind === 'engine' ? '引擎' : '包'}版本 ${version.version}${version.minimumEngineVersion ? ` · 最低引擎版本 ${version.minimumEngineVersion}` : ''} · 文档按版本保存`;
      for (const entry of version.pages) {
        const link = create('a', '', entry.title);
        link.href = manualUrl(product.id, version.version, entry.id);
        if (entry.id === pageId) link.setAttribute('aria-current', 'page');
        pageNav.append(link);
      }
      if (!currentPage) {
        showError('本版本没有这个章节', '请从左侧目录选择当前版本已有的文档。', { text: '返回本版本概览', href: manualUrl(product.id, version.version) });
        return;
      }
      if (typeof currentPage.path !== 'string' || !currentPage.path.startsWith('documentation/') || currentPage.path.split('/').includes('..') || currentPage.path.includes('\\')) throw new Error('Invalid documentation path');
      const pageUrl = new URL(currentPage.path, location.href);
      if (pageUrl.origin !== location.origin) throw new Error('Invalid documentation origin');
      const documentResponse = await fetch(pageUrl.href);
      if (!documentResponse.ok) throw new Error('Document could not be loaded');
      content.replaceChildren(prepareFragment(await documentResponse.text()));
      content.setAttribute('aria-busy', 'false');
      document.title = `${product.name} ${version.version} · ${currentPage.title} · BEngine 文档`;
      const url = new URL(location.href);
      url.search = new URLSearchParams({ product: product.id, version: version.version, doc: currentPage.id }).toString();
      history.replaceState(null, '', url);
      buildSections();
      const currentIndex = version.pages.indexOf(currentPage);
      for (const offset of [-1, 1]) {
        const entry = version.pages[currentIndex + offset];
        if (!entry) continue;
        const link = create('a');
        link.href = manualUrl(product.id, version.version, entry.id);
        link.append(create('small', '', offset === -1 ? '← 上一篇' : '下一篇 →'), document.createTextNode(entry.title));
        pagination.append(link);
      }
      if (location.hash) {
        let hashId;
        try { hashId = decodeURIComponent(location.hash.slice(1)); } catch { hashId = ''; }
        document.getElementById(hashId)?.scrollIntoView({ behavior: 'instant', block: 'start' });
      }
    } catch {
      showError('文档暂时无法加载', '请检查网络后重试。你可以先返回文档中心阅读快速入门。');
    }
  }

  productSelect.addEventListener('change', () => {
    const nextProduct = catalog.products.find(entry => entry.id === productSelect.value);
    if (nextProduct) location.assign(manualUrl(nextProduct.id, nextProduct.defaultVersion));
  });
  versionSelect.addEventListener('change', () => {
    const nextVersion = product.versions.find(entry => entry.version === versionSelect.value);
    if (!nextVersion) return;
    const docId = nextVersion.pages.some(entry => entry.id === currentPage?.id) ? currentPage.id : 'overview';
    location.assign(manualUrl(product.id, nextVersion.version, docId));
  });
  load();
})();
