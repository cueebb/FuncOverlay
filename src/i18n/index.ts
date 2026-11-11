import { en, Translations } from './en';

export type LanguageCode = 'en';

const translations: Record<LanguageCode, Translations> = {
  en,
};

export function getNestedValue(obj: any, path: string): string {
  const keys = path.split('.');
  let current = obj;
  for (const key of keys) {
    if (current && typeof current === 'object' && key in current) {
      current = current[key];
    } else {
      return path; // Fallback to key if not found
    }
  }
  return typeof current === 'string' ? current : path;
}

export function t(key: string, params?: Record<string, string | number>, lang: LanguageCode = 'en'): string {
  const dict = translations[lang] || en;
  let text = getNestedValue(dict, key);

  if (params) {
    Object.entries(params).forEach(([paramKey, value]) => {
      text = text.replace(new RegExp(`\\{${paramKey}\\}`, 'g'), String(value));
    });
  }

  return text;
}
