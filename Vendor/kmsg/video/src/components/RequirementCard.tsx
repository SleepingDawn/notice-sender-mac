import React from "react";
import { useCurrentFrame, spring, interpolate } from "remotion";
import { colors } from "../lib/theme";
import { notoSansKR } from "../lib/fonts";

interface RequirementCardProps {
  icon: string;
  label: string;
  delay?: number;
}

export const RequirementCard: React.FC<RequirementCardProps> = ({
  icon,
  label,
  delay = 0,
}) => {
  const frame = useCurrentFrame();

  const progress = spring({
    frame: frame - delay,
    fps: 30,
    config: {
      damping: 15,
      stiffness: 120,
    },
  });

  const translateY = interpolate(progress, [0, 1], [30, 0]);
  const opacity = interpolate(progress, [0, 1], [0, 1]);

  return (
    <div
      style={{
        background: colors.bgTerminal,
        border: `1px solid ${colors.bgTitleBar}`,
        borderRadius: 12,
        padding: "20px 28px",
        display: "flex",
        alignItems: "center",
        gap: 16,
        width: "100%",
        transform: `translateY(${translateY}px)`,
        opacity,
      }}
    >
      <span style={{ fontSize: 28 }}>{icon}</span>
      <span
        style={{
          fontFamily: notoSansKR,
          fontWeight: 700,
          fontSize: 22,
          color: colors.text,
        }}
      >
        {label}
      </span>
    </div>
  );
};
