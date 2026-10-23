/* Apply saved preferences before loading the stylesheet. */
(() => {
  'use strict';
  const read = key => { try { return localStorage.getItem(key); } catch { return null; } };
  const language = read('bengine-language');
  const theme = read('bengine-theme');
  const root = document.documentElement;
  root.dataset.lang = ['zh', 'en'].includes(language) ? language :
    ((navigator.language || '').toLowerCase().startsWith('zh') ? 'zh' : 'en');
  root.lang = root.dataset.lang === 'zh' ? 'zh-CN' : 'en';
  root.dataset.theme = ['light', 'dark'].includes(theme) ? theme :
    (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
  root.style.colorScheme = root.dataset.theme;
  const themeColor = document.querySelector('meta[name="theme-color"]');
  if (themeColor) themeColor.content = root.dataset.theme === 'dark' ? '#2b2b2b' : '#f6f7f4';
})();
