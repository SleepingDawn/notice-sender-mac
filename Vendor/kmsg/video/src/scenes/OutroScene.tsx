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
import { GlowEffect } from "../components/GlowEffect";

export const OutroScene: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();

  // 0-30: Logo scales up with smooth spring
  const logoSpring = spring({
    frame,
    fps,
    config: { damping: 200 },
  });

  const logoScale = interpolate(logoSpring, [0, 1], [0.8, 1]);
  const logoOpacity = interpolate(logoSpring, [0, 1], [0, 1]);

  // 30-45: GitHub URL fades in
  const urlOpacity = interpolate(frame, [30, 45], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  // 50+: Star badge springs in
  const badgeSpring = spring({
    frame: frame - 50,
    fps,
    config: { damping: 15, stiffness: 120 },
  });

  const badgeY = interpolate(badgeSpring, [0, 1], [20, 0]);
  const badgeOpacity = interpolate(badgeSpring, [0, 1], [0, 1]);

  // 70-85: CTA fades in
  const ctaOpacity = interpolate(frame, [70, 85], [0, 1], {
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
      <GlowEffect color={colors.accent} opacity={0.12} />

      <div
        style={{
          fontFamily: jetBrainsMono,
          fontSize: 72,
          fontWeight: "bold",
          color: colors.accent,
          transform: `scale(${logoScale})`,
          opacity: logoOpacity,
        }}
      >
        kmsg
      </div>

      <div
        style={{
          fontFamily: jetBrainsMono,
          fontSize: 24,
          color: colors.textSecondary,
          opacity: urlOpacity,
          marginTop: 24,
        }}
      >
        github.com/channprj/kmsg
      </div>

      <div
        style={{
          marginTop: 32,
          backgroundColor: colors.kakaoYellow,
          color: "#0D1117",
          fontFamily: notoSansKR,
          fontWeight: 700,
          fontSize: 20,
          padding: "12px 28px",
          borderRadius: 50,
          transform: `translateY(${badgeY}px)`,
          opacity: badgeOpacity,
        }}
      >
        ⭐ Star on GitHub
      </div>

      <div
        style={{
          marginTop: 28,
          fontFamily: notoSansKR,
          fontSize: layout.fontSize.lg,
          fontWeight: 700,
          color: colors.text,
          opacity: ctaOpacity,
        }}
      >
        지금 시작하세요
      </div>
    </AbsoluteFill>
  );
};
