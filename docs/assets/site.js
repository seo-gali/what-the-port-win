'use strict';
document.documentElement.classList.add('js');
const menuButton = document.querySelector('.menu-toggle');
const menu = document.querySelector('#main-nav');
const header = document.querySelector('.site-header');
function sizeHeader() {
  if (header) document.documentElement.style.setProperty('--header-height', `${Math.ceil(header.getBoundingClientRect().height)}px`);
}
if (header) {
  new ResizeObserver(sizeHeader).observe(header);
  sizeHeader();
}
function closeMenu(returnFocus = false) {
  if (!menuButton || !menu) return;
  menu.classList.remove('is-open');
  menuButton.setAttribute('aria-expanded', 'false');
  // Apply the collapsed height before a clicked anchor calculates its scroll offset.
  sizeHeader();
  if (returnFocus) menuButton.focus();
}
menuButton?.addEventListener('click', () => {
  const open = menuButton.getAttribute('aria-expanded') !== 'true';
  menuButton.setAttribute('aria-expanded', String(open));
  menu.classList.toggle('is-open', open);
});
menu?.addEventListener('click', event => { if (event.target.closest('a')) closeMenu(); });
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && menuButton?.getAttribute('aria-expanded') === 'true') closeMenu(true);
});
document.addEventListener('click', event => {
  if (menu && !menu.contains(event.target) && !menuButton.contains(event.target)) closeMenu();
});
window.matchMedia('(min-width:681px)').addEventListener('change', () => closeMenu());

// Follow the visible section without changing the URL, history or keyboard focus.
const sectionLinks = [...(menu?.querySelectorAll('a[href^="#"]') || [])]
  .map(link => ({link, section: document.getElementById(link.hash.slice(1))}))
  .filter(item => item.section);
if (sectionLinks.length) {
  let framePending = false;
  function updateSection() {
    framePending = false;
    const readingLine = (header?.getBoundingClientRect().bottom || 0) + 32;
    let active = null;
    let nearestTop = -Infinity;
    // Navigation order need not match the order of sections on the page.
    for (const item of sectionLinks) {
      const top = item.section.getBoundingClientRect().top;
      if (top <= readingLine && top > nearestTop) { active = item; nearestTop = top; }
    }
    for (const item of sectionLinks) {
      if (item === active) {
        if (item.link.getAttribute('aria-current') !== 'location') item.link.setAttribute('aria-current', 'location');
      } else item.link.removeAttribute('aria-current');
    }
  }
  function scheduleSectionUpdate() {
    if (framePending) return;
    framePending = true;
    requestAnimationFrame(updateSection);
  }
  window.addEventListener('scroll', scheduleSectionUpdate, {passive: true});
  window.addEventListener('resize', scheduleSectionUpdate);
  window.addEventListener('hashchange', scheduleSectionUpdate);
  window.addEventListener('pageshow', scheduleSectionUpdate);
  const sectionResize = new ResizeObserver(scheduleSectionUpdate);
  if (header) sectionResize.observe(header);
  sectionResize.observe(document.querySelector('main'));
  scheduleSectionUpdate();
}
const tabs = [...document.querySelectorAll('[role="tab"]')];
function selectTab(tab, focus = false) {
  for (const item of tabs) {
    const selected = item === tab;
    item.setAttribute('aria-selected', String(selected));
    item.tabIndex = selected ? 0 : -1;
    document.getElementById(item.getAttribute('aria-controls')).hidden = !selected;
  }
  if (focus) tab.focus();
}
tabs.forEach((tab, index) => {
  tab.addEventListener('click', () => selectTab(tab));
  tab.addEventListener('keydown', event => {
    let next = index;
    if (event.key === 'ArrowDown' || event.key === 'ArrowRight') next = (index + 1) % tabs.length;
    else if (event.key === 'ArrowUp' || event.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = tabs.length - 1;
    else return;
    event.preventDefault();selectTab(tabs[next], true);
  });
});
if (tabs.length) selectTab(tabs[0]);
const status = document.getElementById('site-status');
document.querySelectorAll('a[download]').forEach(link => link.addEventListener('click', () => {
  if (status) status.textContent = '다운로드 링크를 열었습니다. 브라우저의 다운로드 목록을 확인하세요.';
}));

// Keep screenshot links useful without JavaScript; with it, view full-size images in place.
const imageLinks = [...document.querySelectorAll('[data-lightbox]')];
if (imageLinks.length) {
  const dialog = document.createElement('dialog');
  dialog.className = 'lightbox';
  dialog.setAttribute('aria-label', '앱 화면 크게 보기');
  dialog.innerHTML = '<div class="lightbox-bar"><p>데모 화면</p><button class="lightbox-close" type="button" autofocus>닫기 ×</button></div><div class="lightbox-scroll" tabindex="0" role="region" aria-label="앱 화면 이미지"><img alt=""></div>';
  document.body.append(dialog);
  let opener;
  const closeButton = dialog.querySelector('button');
  closeButton.addEventListener('click', () => dialog.close());
  dialog.addEventListener('close', () => opener?.focus());
  dialog.addEventListener('click', event => { if (event.target === dialog) dialog.close(); });
  for (const link of imageLinks) link.addEventListener('click', event => {
    if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    event.preventDefault();opener = link;
    const image = dialog.querySelector('img');
    image.src = link.href;image.alt = link.querySelector('img').alt;
    dialog.showModal();closeButton.focus();
    dialog.querySelector('.lightbox-scroll').scrollTo(0, 0);
  });
}
