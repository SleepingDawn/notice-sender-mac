import React from "react";
import { useCurrentFrame, interpolate, AbsoluteFill } from "remotion";
import { colors, layout } from "../lib/theme";
import { jetBrainsMono } from "../lib/fonts";
import { Terminal } from "../components/Terminal";
import { TypingText } from "../components/TypingText";

const commandText = `curl -fL https://github.com/channprj/kmsg/\\
  releases/latest/download/kmsg-macos-universal \\
  -o ~/.local/bin/kmsg && chmod +x ~/.local/bin/kmsg`;

export const InstallScene: React.FC = () => {
  const frame = useCurrentFrame();

  // 0-15: Terminal fades in
  const terminalOpacity = interpolate(frame, [0, 15], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  // Typing starts at frame 15, charFrames=1
  // commandText length determines when typing finishes
  const typingEndFrame = 15 + commandText.length;

  // Success message fades in after typing completes
  const successOpacity = interpolate(
    frame,
    [typingEndFrame, typingEndFrame + 8],
    [0, 1],
    {
      extrapolateLeft: "clamp",
      extrapolateRight: "clamp",
    },
  );

  return (
    <AbsoluteFill
      style={{
        backgroundColor: colors.bg,
        justifyContent: "center",
        alignItems: "center",
      }}
    >
      <Terminal opacity={terminalOpacity}>
        <div
          style={{
            fontFamily: jetBrainsMono,
            fontSize: layout.fontSize.md,
            lineHeight: 1.8,
            whiteSpace: "pre-wrap",
          }}
        >
          <span style={{ color: colors.success }}>$ </span>
          <TypingText text={commandText} startFrame={15} charFrames={1} />
        </div>
        <div
          style={{
            marginTop: 16,
            opacity: successOpacity,
            color: colors.success,
            fontFamily: jetBrainsMono,
            fontSize: layout.fontSize.md,
          }}
        >
          ✓ kmsg installed successfully
        </div>
      </Terminal>
    </AbsoluteFill>
  );
};
