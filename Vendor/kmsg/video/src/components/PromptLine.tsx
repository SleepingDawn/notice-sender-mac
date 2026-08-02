import React from "react";
import { useCurrentFrame, interpolate } from "remotion";
import { colors, layout } from "../lib/theme";
import { jetBrainsMono } from "../lib/fonts";
import { CHAR_FRAMES } from "../lib/constants";
import { TypingText } from "./TypingText";

interface PromptLineProps {
  command: string;
  output?: string[];
  outputColor?: string;
  startFrame?: number;
  charFrames?: number;
  outputDelay?: number;
}

const OUTPUT_FADE_DURATION = 8;
const OUTPUT_STAGGER = 5;

export const PromptLine: React.FC<PromptLineProps> = ({
  command,
  output = [],
  outputColor = colors.text,
  startFrame = 0,
  charFrames = CHAR_FRAMES,
  outputDelay = 8,
}) => {
  const frame = useCurrentFrame();

  const typingEnd = startFrame + command.length * charFrames;
  const outputStart = typingEnd + outputDelay;

  const baseStyle: React.CSSProperties = {
    fontFamily: jetBrainsMono,
    fontSize: layout.fontSize.md,
    lineHeight: 1.6,
    whiteSpace: "pre-wrap",
  };

  return (
    <div>
      {/* Command line */}
      <div style={baseStyle}>
        <span style={{ color: colors.success }}>$ </span>
        <TypingText
          text={command}
          startFrame={startFrame}
          charFrames={charFrames}
          style={{ fontSize: layout.fontSize.md }}
        />
      </div>

      {/* Output lines */}
      {output.map((line, i) => {
        const lineStart = outputStart + i * OUTPUT_STAGGER;
        const opacity = interpolate(
          frame,
          [lineStart, lineStart + OUTPUT_FADE_DURATION],
          [0, 1],
          { extrapolateLeft: "clamp", extrapolateRight: "clamp" },
        );

        return (
          <div key={i} style={{ ...baseStyle, color: outputColor, opacity }}>
            {line}
          </div>
        );
      })}
    </div>
  );
};
