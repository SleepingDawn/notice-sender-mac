import React from "react";
import { useCurrentFrame } from "remotion";
import { colors } from "../lib/theme";
import { jetBrainsMono } from "../lib/fonts";
import { CHAR_FRAMES } from "../lib/constants";
import { Cursor } from "./Cursor";

interface TypingTextProps {
  text: string;
  startFrame?: number;
  charFrames?: number;
  style?: React.CSSProperties;
}

export const TypingText: React.FC<TypingTextProps> = ({
  text,
  startFrame = 0,
  charFrames = CHAR_FRAMES,
  style,
}) => {
  const frame = useCurrentFrame();

  const elapsed = frame - startFrame;
  const charsToShow = Math.min(
    text.length,
    Math.max(0, Math.floor(elapsed / charFrames)),
  );

  const isTyping = charsToShow < text.length && elapsed >= 0;
  // Show cursor briefly after typing finishes (half a second at 30fps)
  const justFinished =
    charsToShow === text.length &&
    elapsed < text.length * charFrames + 15;
  const showCursor = isTyping || justFinished;

  return (
    <span
      style={{
        fontFamily: jetBrainsMono,
        color: colors.text,
        ...style,
      }}
    >
      {text.slice(0, charsToShow)}
      <Cursor show={showCursor} />
    </span>
  );
};
