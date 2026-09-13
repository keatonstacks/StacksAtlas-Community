import { describe, expect, it, vi, afterEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { ThemeProvider, createTheme } from '@mui/material';
import type { ReactNode } from 'react';
import { useIsMobileLayout } from './useIsMobileLayout';

function Wrapper({ children }: { children: ReactNode }) {
  return <ThemeProvider theme={createTheme()}>{children}</ThemeProvider>;
}

function mockMatchMedia(matches: boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }));
}

describe('useIsMobileLayout', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns true when viewport is below md', () => {
    mockMatchMedia(true);
    const { result } = renderHook(() => useIsMobileLayout(), { wrapper: Wrapper });
    expect(result.current).toBe(true);
  });

  it('returns false when viewport is md or wider', () => {
    mockMatchMedia(false);
    const { result } = renderHook(() => useIsMobileLayout(), { wrapper: Wrapper });
    expect(result.current).toBe(false);
  });
});
