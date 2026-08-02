import React from "react";
import { useCurrentFrame, interpolate } from "remotion";
import { CURSOR_BLINK_FRAMES } from "../lib/constants";

interface CursorProps {
  show?: boolean;
  symbol?: string;
}

export const Cursor: React.FC<CursorProps> = ({
  show = true,
  symbol = "\u258C",
}) => {
  const frame = useCurrentFrame();

  if (!show) return null;

  const cyclePosition = frame % CURSOR_BLINK_FRAMES;
  const half = CURSOR_BLINK_FRAMES / 2;

  const opacity = interpolate(
    cyclePosition,
    [0, half - 1, half, CURSOR_BLINK_FRAMES - 1],
    [1, 1, 0, 0],
  );

  return <span style={{ opacity }}>{symbol}</span>;
};
