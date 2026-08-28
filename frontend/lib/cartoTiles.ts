export const CARTO_DARK_TILE_URL =
  'https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png';

export const CARTO_LIGHT_TILE_URL =
  'https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png';

export function cartoTileUrl(
  baseUrl: string,
  apiKey: string | null | undefined
): string {
  const trimmed = apiKey?.trim();
  if (!trimmed) {
    return baseUrl;
  }

  const separator = baseUrl.includes('?') ? '&' : '?';
  return `${baseUrl}${separator}key=${encodeURIComponent(trimmed)}`;
}

export function cartoTilesForAppearance(
  isDark: boolean,
  apiKey: string | null | undefined
): string {
  const baseUrl = isDark ? CARTO_DARK_TILE_URL : CARTO_LIGHT_TILE_URL;
  return cartoTileUrl(baseUrl, apiKey);
}
