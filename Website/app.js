(() => {
  'use strict';
  const root = document.getElementById('exporter-page');
  if (!root) return;
  const managers = {
    alcom: {
      name: 'ALCOM',
      add: '開いた画面で「VPMリポジトリを追加」を押す',
      project: 'アバターのプロジェクトの「管理」を開く',
      package: 'エクスポーターの最新の正式版を選び、「適用」',
      caption: 'ALCOMのパッケージ一覧（画面内は変更前の名称）',
      download: 'https://vrc-get.anatawa12.com/alcom/',
      fallback: 'ALCOMの「パッケージ&テンプレート」→「VPMリポジトリ」→「VPMリポジトリを追加」を開き、次のURLを貼り付けて「VPMリポジトリを追加」を押します。表示された内容を確認し、もう一度「VPMリポジトリを追加」を押します。',
    },
    vcc: {
      name: 'VCC',
      add: '開いた画面で「I Understand, Add Repository」を押す',
      project: 'アバターのプロジェクトの「Manage Project」を開く',
      package: 'エクスポーターの「Installed Version」で最新の正式版を選ぶ',
      caption: 'VCCのプロジェクト一覧（実画面）',
      download: 'https://vcc.docs.vrchat.com/',
      fallback: 'VCCの「Settings」→「Packages」→「Add Repository」を開き、次のURLを貼り付けます。「Add」を押し、内容を確認して「I Understand, Add Repository」を押します。',
    },
  };
  let manager = 'alcom';
  const managerButtons = [...root.querySelectorAll('[data-manager]')];
  function selectManager(value) {
    if (!Object.hasOwn(managers, value)) return;
    manager = value;
    const copy = managers[value];
    managerButtons.forEach(button => button.setAttribute('aria-pressed', String(button.dataset.manager === value)));
    root.querySelectorAll('[data-manager-copy]').forEach(node => { node.textContent = copy[node.dataset.managerCopy]; });
    root.querySelectorAll('[data-manager-shot]').forEach(node => { node.hidden = node.dataset.managerShot !== value; });
    root.querySelector('[data-manager-caption]').textContent = copy.caption;
    root.querySelector('[data-manager-cta]').textContent = copy.name + 'で追加';
    root.querySelector('[data-manager-fallback]').textContent = copy.fallback;
    const download = root.querySelector('[data-manager-download]');
    download.href = copy.download;
    download.textContent = copy.name + 'をまだ入れていない方はこちら';
  }
  managerButtons.forEach(button => button.addEventListener('click', () => selectManager(button.dataset.manager)));
  root.querySelectorAll('[data-scroll]').forEach(button => button.addEventListener('click', () => {
    root.querySelector('[data-section="' + button.dataset.scroll + '"]').scrollIntoView({ block: 'start' });
  }));
  const dialog = root.querySelector('dialog');
  const body = root.querySelector('[data-dialog-body]');
  const docs = 'https://vcc.docs.vrchat.com/guides/community-repositories/';
  const shots = {
    alcom: [{ file: 'alcom-install.webp', alt: 'ALCOMの管理画面とインストールするパッケージの一覧', caption: 'プロジェクトの「管理」で、追加したエクスポーターの「インストール済み」欄から最新の正式版を選び、変更内容を確認して「適用」を押します。画面内のエクスポーター名は変更前のものです。' }],
    vcc: [{ file: 'vcc-add-repository.webp', alt: 'VCCの配布元追加確認画面', caption: '内容を確認して「I Understand, Add Repository」を押します。', source: docs }, { file: 'vcc-projects.webp', alt: '実際のVCCのProjects画面', caption: '「Projects」でアバターを入れたプロジェクトを探し、「Manage Project」を開きます（実画面）。' }, { file: 'vcc-installed-version.webp', alt: 'VCCのInstalled Version選択欄', caption: '追加したエクスポーターの「Installed Version」欄から、最新の正式版を選びます。導入済みの場合も、更新がないか確認してください。', source: docs }],
  };
  function openImages(key, title) {
    if (!Object.hasOwn(shots, key)) return;
    body.replaceChildren();
    const heading = document.createElement('h2');
    heading.textContent = title;
    body.append(heading);
    shots[key].forEach(shot => {
      const figure = document.createElement('figure');
      const frame = document.createElement('div');
      if (shot.className) frame.className = shot.className;
      const image = document.createElement('img');
      image.src = 'assets/guide/' + shot.file;
      image.alt = shot.alt;
      image.decoding = 'async';
      frame.append(image);
      const caption = document.createElement('figcaption');
      caption.textContent = shot.caption;
      if (shot.source) {
        const source = document.createElement('a');
        source.href = shot.source;
        source.target = '_blank';
        source.rel = 'noopener noreferrer';
        source.textContent = 'VCC公式の参考画像';
        caption.append(document.createTextNode(' '), source);
      }
      figure.append(frame, caption);
      body.append(figure);
    });
    dialog.setAttribute('aria-label', title);
    dialog.showModal();
    dialog.scrollTop = 0;
  }
  root.querySelector('[data-install-guide]').addEventListener('click', () => openImages(manager, managers[manager].name + 'でインストールする'));
  root.querySelector('[data-close]').addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', event => {
    const bounds = dialog.getBoundingClientRect();
    if (event.target === dialog && (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom)) dialog.close();
  });
})();
