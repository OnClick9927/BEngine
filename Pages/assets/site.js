(() => {
  'use strict';
  const root = document.documentElement;
  const languageButton = document.querySelector('[data-language-toggle]');
  const themeButton = document.querySelector('[data-theme-toggle]');
  const menuButton = document.querySelector('[data-menu-toggle]');
  const mobileNav = document.querySelector('[data-mobile-nav]');
  const status = document.getElementById('site-status');
  const choose = (zh, en) => root.dataset.lang === 'zh' ? zh : en;
  const store = (key, value) => { try { localStorage.setItem(key, value); } catch { /* Still usable for this page. */ } };

  function updateControls() {
    const english = root.dataset.lang === 'en';
    const dark = root.dataset.theme === 'dark';
    languageButton.textContent = english ? 'ZH' : 'EN';
    languageButton.setAttribute('aria-label', english ? 'Switch to Chinese' : 'Switch to English');
    languageButton.title = languageButton.getAttribute('aria-label');
    const themeLabel = dark ? choose('切换到浅色模式', 'Switch to light mode') : choose('切换到深色模式', 'Switch to dark mode');
    themeButton.setAttribute('aria-label', themeLabel);
    themeButton.title = themeLabel;
    themeButton.setAttribute('aria-pressed', String(dark));
    menuButton.setAttribute('aria-label', menuButton.getAttribute('aria-expanded') === 'true' ?
      choose('关闭导航菜单', 'Close navigation menu') : choose('打开导航菜单', 'Open navigation menu'));
    document.querySelector('meta[name="theme-color"]').content = dark ? '#2b2b2b' : '#f6f7f4';
  }

  function applyLanguage(language) {
    root.dataset.lang = language;
    root.lang = language === 'zh' ? 'zh-CN' : 'en';
    status.textContent = '';
    document.querySelectorAll('[data-zh][data-en]').forEach(element => {
      // Both translations are static authored markup. Catalog data uses textContent.
      element.innerHTML = element.getAttribute(`data-${language}`);
    });
    for (const attribute of ['content', 'aria-label', 'alt', 'placeholder']) {
      document.querySelectorAll(`[data-zh-${attribute}][data-en-${attribute}]`).forEach(element => {
        element.setAttribute(attribute, element.getAttribute(`data-${language}-${attribute}`));
      });
    }
    document.querySelector('meta[property="og:locale"]').content = language === 'zh' ? 'zh_CN' : 'en_US';
    updateControls();
    renderPackages();
  }
  function applyTheme(theme) {
    root.dataset.theme = theme;
    root.style.colorScheme = theme;
    updateControls();
  }
  function closeMenu() {
    menuButton.setAttribute('aria-expanded', 'false');
    mobileNav.hidden = true;
    document.body.classList.remove('menu-open');
    updateControls();
  }
  languageButton.addEventListener('click', () => {
    const language = root.dataset.lang === 'zh' ? 'en' : 'zh';
    store('bengine-language', language);
    applyLanguage(language);
    status.textContent = choose('已切换为中文', 'Language changed to English');
  });
  themeButton.addEventListener('click', () => {
    const theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
    store('bengine-theme', theme);
    applyTheme(theme);
    status.textContent = theme === 'dark' ? choose('已切换为深色模式', 'Dark mode enabled') : choose('已切换为浅色模式', 'Light mode enabled');
  });
  menuButton.addEventListener('click', () => {
    const open = menuButton.getAttribute('aria-expanded') !== 'true';
    menuButton.setAttribute('aria-expanded', String(open));
    mobileNav.hidden = !open;
    document.body.classList.toggle('menu-open', open);
    updateControls();
  });
  mobileNav.addEventListener('click', event => { if (event.target.closest('a')) closeMenu(); });
  document.addEventListener('keydown', event => {
    if (mobileNav.hidden) return;
    if (event.key === 'Escape') { closeMenu(); menuButton.focus(); }
    if (event.key === 'Tab') {
      const focusable = [...document.querySelectorAll('.header-actions button, .header-actions a'), ...mobileNav.querySelectorAll('a')]
        .filter(element => element.getClientRects().length);
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    }
  });
  window.matchMedia('(min-width: 1101px)').addEventListener('change', event => { if (event.matches) closeMenu(); });
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', event => {
    let savedTheme;
    try { savedTheme = localStorage.getItem('bengine-theme'); } catch { /* Use the system preference. */ }
    if (!['light', 'dark'].includes(savedTheme)) applyTheme(event.matches ? 'dark' : 'light');
  });
  window.addEventListener('storage', event => {
    if (event.key === 'bengine-language' && ['zh', 'en'].includes(event.newValue)) applyLanguage(event.newValue);
    if (event.key === 'bengine-theme' && ['light', 'dark'].includes(event.newValue)) applyTheme(event.newValue);
  });

  const grid = document.getElementById('package-grid');
  const search = document.getElementById('package-search');
  const count = document.getElementById('package-count');
  const results = document.getElementById('package-results');
  const categoryTabs = [...document.querySelectorAll('[data-package-category]')];
  const categoryIds = new Set(categoryTabs.map(tab => tab.dataset.packageCategory).filter(id => id !== 'all'));
  let selectedCategory = 'all';
  let packages = [];
  let packageState = 'loading';
  const node = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
  };
  function matchesSearch(pkg, term) {
    return [pkg.name, pkg.nameEn, pkg.description, pkg.descriptionEn]
      .some(value => value.toLocaleLowerCase().includes(term));
  }
  function updateCategoryTabs(matching) {
    for (const tab of categoryTabs) {
      const category = tab.dataset.packageCategory;
      const active = category === selectedCategory;
      tab.setAttribute('aria-selected', String(active));
      tab.tabIndex = active ? 0 : -1;
      tab.querySelector('.catalog-tab-count').textContent = packageState === 'ready' ?
        String(matching.filter(pkg => category === 'all' || pkg.categories.includes(category)).length) : '—';
      if (active) results.setAttribute('aria-labelledby', tab.id);
    }
  }
  function selectCategory(category) {
    selectedCategory = category;
    renderPackages();
  }
  function renderPackages() {
    if (!grid) return;
    const term = search.value.trim().toLocaleLowerCase();
    const matching = packages.filter(pkg => matchesSearch(pkg, term));
    updateCategoryTabs(matching);
    grid.replaceChildren();
    if (packageState === 'loading') {
      count.textContent = choose('正在加载包目录…', 'Loading packages…');
      grid.append(node('p', 'catalog-message', choose('正在准备你的工具箱…', 'Preparing your toolkit…')));
      return;
    }
    if (packageState === 'error') {
      count.textContent = choose('包目录未加载', 'Packages could not be loaded');
      const error = node('div', 'empty-state');
      error.append(node('h2', '', choose('包目录暂时无法加载', 'Packages are unavailable right now')),
        node('p', '', choose('检查网络后重试，或在 Hub 中浏览可用包。', 'Check your connection and try again, or browse available packages in Hub.')));
      const retry = node('button', 'button button-dark', choose('重新加载', 'Try again'));
      retry.type = 'button'; retry.addEventListener('click', loadPackages);
      error.append(retry); grid.append(error); return;
    }
    const visible = matching.filter(pkg => selectedCategory === 'all' || pkg.categories.includes(selectedCategory));
    count.textContent = choose(`显示 ${visible.length} / ${packages.length} 项`, `Showing ${visible.length} of ${packages.length}`);
    if (!visible.length) {
      const empty = node('div', 'empty-state');
      empty.append(node('h2', '', choose('没有找到匹配的包', 'No matching packages')),
        node('p', '', choose('换个关键词或分类，或清除搜索后再看看。', 'Try another keyword or category, or clear your search.')));
      const actions = node('div', 'button-row');
      const reset = node('button', 'button button-outline', choose('清除搜索', 'Clear search'));
      reset.type = 'button';
      reset.addEventListener('click', () => { search.value = ''; renderPackages(); search.focus(); });
      actions.append(reset);
      if (selectedCategory !== 'all') {
        const resetAll = node('button', 'button button-outline', choose('查看全部扩展包', 'Show all packages'));
        resetAll.type = 'button';
        resetAll.addEventListener('click', () => {
          search.value = ''; selectCategory('all'); categoryTabs[0].focus();
        });
        actions.append(resetAll);
      }
      empty.append(actions); grid.append(empty); return;
    }
    const fragment = document.createDocumentFragment();
    for (const pkg of visible) {
      const card = node('article', 'package-card');
      // Public cards contain only a name and a short description.
      card.append(node('h2', '', choose(pkg.name, pkg.nameEn)), node('p', '', choose(pkg.description, pkg.descriptionEn)));
      fragment.append(card);
    }
    grid.append(fragment);
  }
  async function loadPackages() {
    packageState = 'loading'; grid.setAttribute('aria-busy', 'true'); renderPackages();
    try {
      const response = await fetch('data/packages.json?v=13');
      if (!response.ok) throw new Error('Catalog response failed');
      const data = await response.json();
      if (!Array.isArray(data) || !data.length || data.some(pkg =>
        !['name', 'nameEn', 'description', 'descriptionEn'].every(key => typeof pkg[key] === 'string' && pkg[key]) ||
        !Array.isArray(pkg.categories) || !pkg.categories.length || pkg.categories.some(category => !categoryIds.has(category)))) throw new Error('Invalid catalog');
      packages = data; packageState = 'ready';
    } catch { packageState = 'error'; }
    finally { grid.setAttribute('aria-busy', 'false'); renderPackages(); }
  }
  applyLanguage(root.dataset.lang === 'en' ? 'en' : 'zh');
  if (grid) {
    search.addEventListener('input', renderPackages);
    categoryTabs.forEach((tab, index) => {
      tab.addEventListener('click', () => selectCategory(tab.dataset.packageCategory));
      tab.addEventListener('keydown', event => {
        let next;
        if (event.key === 'ArrowRight') next = (index + 1) % categoryTabs.length;
        else if (event.key === 'ArrowLeft') next = (index - 1 + categoryTabs.length) % categoryTabs.length;
        else if (event.key === 'Home') next = 0;
        else if (event.key === 'End') next = categoryTabs.length - 1;
        else return;
        event.preventDefault();
        categoryTabs[next].focus();
        selectCategory(categoryTabs[next].dataset.packageCategory);
      });
    });
    loadPackages();
  }
})();
