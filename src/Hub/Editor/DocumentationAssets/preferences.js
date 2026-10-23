// Run before the stylesheet to match the Hub's appearance on the first paint.
(() => {
  const query = new URLSearchParams(location.search);
  const read = key => { try { return localStorage.getItem(key); } catch { return null; } };
  const language = (read('bengine-language') || query.get('lang') || navigator.language).toLowerCase().startsWith('en') ? 'en' : 'zh';
  const stored = read('bengine-theme');
  const requested = query.get('theme');
  const theme = ['light','dark'].includes(stored) ? stored : ['light','dark'].includes(requested) ? requested : matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  document.documentElement.lang = language === 'en' ? 'en' : 'zh-CN';
  document.documentElement.dataset.lang = language;
  document.documentElement.dataset.theme = theme;
})();
