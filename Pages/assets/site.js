(() => {
  'use strict';

  const menuButton = document.querySelector('[data-menu-toggle]');
  const mobileNav = document.querySelector('[data-mobile-nav]');
  const closeMenu = () => {
    if (!menuButton || !mobileNav) return;
    menuButton.setAttribute('aria-expanded', 'false');
    menuButton.setAttribute('aria-label', '打开导航菜单');
    mobileNav.hidden = true;
    document.body.classList.remove('menu-open');
  };
  if (menuButton && mobileNav) {
    menuButton.addEventListener('click', () => {
      const open = menuButton.getAttribute('aria-expanded') !== 'true';
      menuButton.setAttribute('aria-expanded', String(open));
      menuButton.setAttribute('aria-label', open ? '关闭导航菜单' : '打开导航菜单');
      mobileNav.hidden = !open;
      document.body.classList.toggle('menu-open', open);
    });
    mobileNav.addEventListener('click', event => {
      if (event.target.closest('a')) closeMenu();
    });
    document.addEventListener('keydown', event => {
      if (event.key === 'Escape' && !mobileNav.hidden) {
        closeMenu();
        menuButton.focus();
      }
      if (event.key === 'Tab' && !mobileNav.hidden) {
        const focusable = [menuButton, ...mobileNav.querySelectorAll('a')];
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault();
          last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault();
          first.focus();
        }
      }
    });
    window.matchMedia('(min-width: 801px)').addEventListener('change', event => {
      if (event.matches) closeMenu();
    });
  }

  document.querySelectorAll('[data-copy]').forEach(button => {
    button.addEventListener('click', async () => {
      const code = document.getElementById(button.dataset.copy);
      if (!code) return;
      const announcement = document.getElementById('copy-status');
      try {
        await navigator.clipboard.writeText(code.textContent.trim());
        button.textContent = '已复制';
        if (announcement) announcement.textContent = '命令已复制到剪贴板';
        window.setTimeout(() => { button.textContent = '复制'; }, 2000);
      } catch {
        const selection = window.getSelection();
        const range = document.createRange();
        range.selectNodeContents(code);
        selection.removeAllRanges();
        selection.addRange(range);
        button.textContent = '请手动复制';
        if (announcement) announcement.textContent = '已选中命令，请按 Ctrl+C 或使用系统复制功能';
      }
    });
  });

  const grid = document.getElementById('package-grid');
  if (!grid) return;
  const search = document.getElementById('package-search');
  const filterRow = document.getElementById('package-filters');
  const count = document.getElementById('package-count');
  let packages = [];
  let activeCategory = '全部';

  const node = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
  };

  function render() {
    const term = search.value.trim().toLocaleLowerCase('zh-CN');
    const visible = packages.filter(pkg =>
      (activeCategory === '全部' || pkg.category === activeCategory) &&
      [pkg.name, pkg.id, pkg.description, pkg.category].some(value => value.toLocaleLowerCase('zh-CN').includes(term))
    );
    grid.replaceChildren();
    count.textContent = `显示 ${visible.length} / ${packages.length} 个包`;
    if (!visible.length) {
      const empty = node('div', 'empty-state');
      empty.append(node('h2', '', '暂时没有找到匹配的包'), node('p', '', '换个关键词，或清除筛选后再看看。'));
      const reset = node('button', 'button button-outline', '清除筛选');
      reset.type = 'button';
      reset.addEventListener('click', () => {
        search.value = '';
        activeCategory = '全部';
        updateFilterButtons();
        render();
        search.focus();
      });
      empty.append(reset);
      grid.append(empty);
      return;
    }
    const fragment = document.createDocumentFragment();
    for (const pkg of visible) {
      const card = node('article', 'package-card');
      const top = node('div', 'package-card-top');
      const initials = pkg.name.replace(/[^a-zA-Z0-9]/g, '').slice(0, 2).toUpperCase();
      const symbol = node('span', 'package-symbol', initials);
      symbol.setAttribute('aria-hidden', 'true');
      top.append(symbol, node('span', 'package-version', `v${pkg.version}`));
      const bottom = node('div', 'package-card-bottom');
      const documentation = node('a', '', '查看文档 ↗');
      documentation.href = pkg.docsUrl;
      documentation.setAttribute('aria-label', `${pkg.name} 文档`);
      bottom.append(node('span', '', pkg.category), documentation);
      card.append(top, node('h2', '', pkg.name), node('div', 'package-id', pkg.id), node('p', '', pkg.description), bottom);
      fragment.append(card);
    }
    grid.append(fragment);
  }

  function updateFilterButtons() {
    filterRow.querySelectorAll('button').forEach(button => {
      button.setAttribute('aria-pressed', String(button.dataset.category === activeCategory));
    });
  }

  function showError() {
    grid.replaceChildren();
    const error = node('div', 'empty-state');
    error.append(node('h2', '', '包目录暂时无法加载'), node('p', '', '请检查网络后重试，也可以直接查看 GitHub 包目录。'));
    const retry = node('button', 'button button-dark', '重新加载');
    retry.type = 'button';
    retry.addEventListener('click', loadPackages);
    const source = node('a', 'text-link', '查看 GitHub 包目录 ↗');
    source.href = 'https://github.com/OnClick9927/BEngine/tree/main/src/Packages';
    const actions = node('div', 'button-row');
    actions.style.justifyContent = 'center';
    actions.append(retry, source);
    error.append(actions);
    grid.append(error);
    count.textContent = '包目录未加载';
  }

  async function loadPackages() {
    grid.setAttribute('aria-busy', 'true');
    count.textContent = '正在加载包目录…';
    try {
      const response = await fetch('data/packages.json');
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const data = await response.json();
      if (!Array.isArray(data) || !data.length || data.some(pkg =>
        !['id', 'name', 'version', 'category', 'description', 'docsUrl'].every(key => typeof pkg[key] === 'string') ||
        !pkg.docsUrl.startsWith('https://github.com/OnClick9927/BEngine/'))) throw new Error('Invalid package catalog');
      packages = data;
      filterRow.replaceChildren();
      for (const category of ['全部', ...new Set(packages.map(pkg => pkg.category))]) {
        const button = node('button', 'filter-button', category);
        button.type = 'button';
        button.dataset.category = category;
        button.addEventListener('click', () => {
          activeCategory = category;
          updateFilterButtons();
          render();
        });
        filterRow.append(button);
      }
      updateFilterButtons();
      render();
    } catch {
      showError();
    } finally {
      grid.setAttribute('aria-busy', 'false');
    }
  }
  search.addEventListener('input', render);
  loadPackages();
})();
