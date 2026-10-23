(() => {
  const root = document.getElementById('exporter-qr-simple');
  if (!root) return;
  const start = root.querySelector('#qrs-start');
  const install = root.querySelector('#qrs-install');
  const buttons = [...root.querySelectorAll('[data-manager]')];
  function selectManager(manager) {
    buttons.forEach(button => {
      const active = button.dataset.manager === manager;
      button.setAttribute('aria-pressed', String(active));
      root.querySelector('#qrs-' + button.dataset.manager).hidden = !active;
    });
  }
  buttons.forEach(button => button.addEventListener('click', () => selectManager(button.dataset.manager)));
  install.addEventListener('toggle', () => start.setAttribute('aria-expanded', String(install.open)));
  start.addEventListener('click', () => {
    install.open = true;
    start.setAttribute('aria-expanded', 'true');
    install.scrollIntoView({block: 'start'});
  });
})();
