import React from "react";
import {
  useCurrentFrame,
  useVideoConfig,
  interpolate,
  spring,
  AbsoluteFill,
} from "remotion";
import { colors, layout } from "../lib/theme";
import { jetBrainsMono, notoSansKR } from "../lib/fonts";
import { Terminal } from "../components/Terminal";
import { TypingText } from "../components/TypingText";
import { GlowEffect } from "../components/GlowEffect";

export const IntroScene: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();

  // 0-20: GlowEffect fades in
  const glowOpacity = interpolate(frame, [0, 20], [0, 0.15], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  // 10-30: Terminal fades in
  const terminalOpacity = interpolate(frame, [10, 30], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  // 70-150: Tagline slides up with spring
  const tagSpring = spring({
    frame: frame - 70,
    fps,
    config: { damping: 15, stiffness: 120 },
  });

  const tagY = interpolate(tagSpring, [0, 1], [30, 0]);
  const tagOpacity = interpolate(tagSpring, [0, 1], [0, 1]);

  return (
    <AbsoluteFill
      style={{
        backgroundColor: colors.bg,
        justifyContent: "center",
        alignItems: "center",
      }}
    >
      <GlowEffect color={colors.accent} x={540} y={540} opacity={glowOpacity} />
      <Terminal opacity={terminalOpacity}>
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            minHeight: 360,
          }}
        >
          <div
            style={{
              fontSize: layout.fontSize.xxl,
              fontWeight: 700,
              fontFamily: jetBrainsMono,
            }}
          >
            <TypingText
              text="kmsg"
              startFrame={30}
              style={{ color: colors.accent, fontSize: layout.fontSize.xxl, fontWeight: 700 }}
            />
          </div>
          <div
            style={{
              transform: `translateY(${tagY}px)`,
              opacity: tagOpacity,
              marginTop: 24,
              fontFamily: notoSansKR,
              fontSize: layout.fontSize.lg,
              color: colors.textSecondary,
            }}
          >
            macOS에서 카카오톡 메시지를 CLI로
          </div>
        </div>
      </Terminal>
    </AbsoluteFill>
  );
};
