import { useEffect, useMemo, useState, type ReactNode, type RefObject } from 'react';
import { Box } from '@mui/material';

/** Approximate row height for 0.85rem mono @ line-height 1.6 */
const ROW_HEIGHT_PX = 22;
const OVERSCAN_ROWS = 10;

export interface VirtualLogListProps {
  lines: string[];
  containerRef: RefObject<HTMLDivElement | null>;
  renderLine: (line: string, index: number) => ReactNode;
}

export function VirtualLogList({ lines, containerRef, renderLine }: VirtualLogListProps) {
  const [range, setRange] = useState({ start: 0, end: 60 });

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;

    const updateRange = () => {
      const scrollTop = el.scrollTop;
      const viewHeight = el.clientHeight;
      const start = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT_PX) - OVERSCAN_ROWS);
      const visibleRows = Math.ceil(viewHeight / ROW_HEIGHT_PX) + OVERSCAN_ROWS * 2;
      const end = Math.min(lines.length, start + visibleRows);
      setRange((prev) => (prev.start === start && prev.end === end ? prev : { start, end }));
    };

    updateRange();
    el.addEventListener('scroll', updateRange, { passive: true });
    const observer = new ResizeObserver(updateRange);
    observer.observe(el);

    return () => {
      el.removeEventListener('scroll', updateRange);
      observer.disconnect();
    };
  }, [containerRef, lines.length]);

  const visibleLines = useMemo(
    () => lines.slice(range.start, range.end),
    [lines, range.start, range.end]
  );

  const topSpacer = range.start * ROW_HEIGHT_PX;
  const bottomSpacer = Math.max(0, (lines.length - range.end) * ROW_HEIGHT_PX);

  return (
    <Box>
      {topSpacer > 0 && <Box sx={{ height: topSpacer }} aria-hidden />}
      {visibleLines.map((line, offset) => {
        const index = range.start + offset;
        return (
          <Box key={index} sx={{ minHeight: ROW_HEIGHT_PX }}>
            {renderLine(line, index)}
          </Box>
        );
      })}
      {bottomSpacer > 0 && <Box sx={{ height: bottomSpacer }} aria-hidden />}
    </Box>
  );
}
