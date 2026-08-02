import React from "react";
import {
  useCurrentFrame,
  useVideoConfig,
  interpolate,
  spring,
  AbsoluteFill,
} from "remotion";
import { colors, layout } from "../lib/theme";
import { notoSansKR } from "../lib/fonts";
import { RequirementCard } from "../components/RequirementCard";
import { GlowEffect } from "../components/GlowEffect";

export const RequirementsScene: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();

  // 0-15: Title spring slide-up
  const titleSpring = spring({
    frame,
    fps,
    config: { damping: 200 },
  });

  const titleY = interpolate(titleSpring, [0, 1], [20, 0]);
  const titleOpacity = interpolate(titleSpring, [0, 1], [0, 1]);

  return (
    <AbsoluteFill
      style={{
        backgroundColor: colors.bg,
        justifyContent: "center",
        alignItems: "center",
        padding: layout.terminalPadding,
      }}
    >
      <GlowEffect color={colors.kakaoYellow} opacity={0.08} y={400} />

      <div
        style={{
          fontFamily: notoSansKR,
          fontSize: layout.fontSize.xl,
          fontWeight: 700,
          color: colors.text,
          marginBottom: 40,
          transform: `translateY(${titleY}px)`,
          opacity: titleOpacity,
        }}
      >
        요구사항
      </div>

      <div
        style={{
          display: "flex",
          flexDirection: "column",
          gap: 16,
          width: layout.terminalWidth,
        }}
      >
        <RequirementCard icon="🖥️" label="macOS 13 Ventura 이상" delay={15} />
        <RequirementCard
          icon="💬"
          label="KakaoTalk 데스크톱 설치"
          delay={30}
        />
        <RequirementCard
          icon="♿"
          label="손쉬운 사용 권한 허용"
          delay={45}
        />
      </div>
    </AbsoluteFill>
  );
};
