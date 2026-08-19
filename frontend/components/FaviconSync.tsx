'use client';

import { useEffect } from 'react';

const LIGHT_ICON = '/tempo-mark-ink.png';
const DARK_ICON = '/tempo-mark-volt.png';

function ensureIconLink(): HTMLLinkElement {
  const existing = document.querySelector<HTMLLinkElement>(
    'link[rel="icon"][data-tempo-favicon]'
  );
  if (existing) {
    return existing;
  }

  document
    .querySelectorAll('link[rel="icon"], link[rel="shortcut icon"]')
    .forEach((node) => node.remove());

  const link = document.createElement('link');
  link.rel = 'icon';
  link.type = 'image/png';
  link.setAttribute('data-tempo-favicon', '');
  document.head.appendChild(link);
  return link;
}

function syncFavicon() {
  const dark = document.documentElement.classList.contains('dark');
  ensureIconLink().href = dark ? DARK_ICON : LIGHT_ICON;
}

export function FaviconSync() {
  useEffect(() => {
    syncFavicon();
    const observer = new MutationObserver(syncFavicon);
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['class'],
    });
    return () => observer.disconnect();
  }, []);

  return null;
}
