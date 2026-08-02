import React from "react";
import { useCurrentFrame, interpolate, AbsoluteFill } from "remotion";
import { colors, layout } from "../lib/theme";
import { jetBrainsMono } from "../lib/fonts";
import { Terminal } from "../components/Terminal";
import { TypingText } from "../components/TypingText";
import { JsonBlock } from "../components/JsonBlock";

const jsonString = `{
  "messages": [
    {
      "time": "14:30",
      "sender": "\uD76C\uCC2C",
      "text": "\uC548\uB155\uD558\uC138\uC694"
    }
  ]
}`;

export const JsonScene: React.FC = () => {
  const frame = useCurrentFrame();

  // 0-10: Terminal fades in
  const terminalOpacity = interpolate(frame, [0, 10], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

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
            lineHeight: 1.6,
            whiteSpace: "pre-wrap",
          }}
        >
          <span style={{ color: colors.success }}>$ </span>
          <TypingText text={'kmsg read "\uD76C\uCC2C" --json'} startFrame={10} />
        </div>
        <div style={{ marginTop: 16 }}>
          <JsonBlock json={jsonString} startFrame={70} lineDelay={4} />
        </div>
      </Terminal>
    </AbsoluteFill>
  );
};
