import { lazy, type ComponentType, type LazyExoticComponent } from 'react';

type LazyRouteModule = () => Promise<{ default: ComponentType<any> }>;

/**
 * Lazy route loader that reloads once when a hashed chunk 404s (stale index.html after deploy).
 */
export function lazyWithRetry(factory: LazyRouteModule): LazyExoticComponent<ComponentType<any>> {
  return lazy(async () => {
    const reloadKey = 'stacksatlas-chunk-reload';
    try {
      const module = await factory();
      sessionStorage.removeItem(reloadKey);
      return module;
    } catch (error) {
      const isChunkError =
        error instanceof TypeError &&
        /Failed to fetch dynamically imported module|Importing a module script failed/i.test(
          error.message,
        );

      if (isChunkError && !sessionStorage.getItem(reloadKey)) {
        sessionStorage.setItem(reloadKey, '1');
        window.location.reload();
        return new Promise<{ default: ComponentType<any> }>(() => {});
      }

      sessionStorage.removeItem(reloadKey);
      throw error;
    }
  });
}
